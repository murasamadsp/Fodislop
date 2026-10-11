#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Networking.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking.Auth;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using Unity.Profiling;
using UnityEngine;
using VContainer;

namespace Kern.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour, IConnectionService, IWorldRegionRequester
    {
        private static readonly ProfilerMarker s_packetDrainMarker =
            new("Kern.Net.DrainPacketQueue");

        private static readonly AllocationLedger.Entry s_allocationEntry =
            AllocationLedger.Register("Сеть — разбор очереди");

        // Бюджет на обработку входящих пакетов — доля времени КАДРА, а не стены часов.
        // Пропорция к deltaTime масштабирует пропускную способность с частотой кадров
        // (независимость от 30 vs 144 FPS) и ограничивает долю CPU, но при этом
        // всплеск пакетов (мировые текстуры при входе в мир) дренится за несколько
        // кадров, а не тянется секунду, как с накоплением 2% реального времени.
        private const float PacketDrainBudgetFractionOfFrame = 0.33f;
        private const float PacketDrainBudgetMaximumSeconds = 0.01f;

        public IServerConnection? Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus == ConnectionStatus.Connected;
        public bool IsAutoReconnecting => _shouldAutoReconnect;
        public bool IsWorldStartupPending => _isWorldStartupPending;
        public bool IsOffline => Connection is IOfflineConnection;

        public void RequestWorldRegion(string worldCodeName, RectInt serverRegion)
        {
            if (IsConnected && Connection is IWorldRegionRequester requester)
            {
                requester.RequestWorldRegion(worldCodeName, serverRegion);
            }
        }

        private bool _useOldClient;
        public event Action<ServerPacket>? OnPacketReceived;
        public event Action? OnPacketBatchStarted;
        public event Action? OnPacketBatchCompleted;
        public event Action? OnTransportDisconnected;
        public event Action<string>? OnReconnectStatusChanged;
        public event Action<string>? OnDisconnectReason;
        public event Action? OnReconnectHidden;

        private readonly InboundPacketBuffer _inboundPackets = new();
        private readonly ReconnectBackoff _reconnectBackoff = new();

        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IGameTokenStore _tokens = null!;

        [Inject]
        private DummyConnection _dummyConnection = null!;
        [Inject]
        private ConnectionTransportFactory _transportFactory = null!;

        private bool _shouldAutoReconnect;
        private bool _isConnectionAttemptPending;
        private bool _isWorldStartupPending;
        private bool _clientHelloSent;
        private float _reconnectCountdown;
        private string _reconnectStatus = string.Empty;
        private bool _restartWorldOnConnect;

        // НУЖЕН: сохраняет причину серверного дисконнекта — используется при реконнекте
        // и для диагностики в ReconnectUI. НЕ УДАЛЯТЬ (см. HandleServerDisconnect).
        private string _disconnectReason = string.Empty;

        protected void Awake()
        {
            _inboundPackets.CaptureMainThread();
        }

        protected void OnDestroy()
        {
            Disconnect();
        }

        protected void Update()
        {
            DrainPacketQueue();
            UpdateReconnect();
        }

        private void DrainPacketQueue()
        {
            using var marker = s_packetDrainMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(s_allocationEntry);
            float budgetSeconds = Mathf.Min(
                Time.unscaledDeltaTime * PacketDrainBudgetFractionOfFrame,
                PacketDrainBudgetMaximumSeconds);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int processedCount = 0;
            bool stoppedByBudget = false;
            bool stoppedByCap = false;

            bool batchActive = _inboundPackets.Count > 1;
            if (batchActive)
            {
                OnPacketBatchStarted?.Invoke();
            }

            try
            {
                while (processedCount < ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame)
                {
                    // A handler cannot be preempted. Stop before dequeuing the next
                    // packet so one expensive handler is the only unavoidable overrun.
                    float elapsedMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    if (processedCount > 0 && elapsedMs >= budgetSeconds * 1000f)
                    {
                        stoppedByBudget = true;
                        break;
                    }

                    if (!_inboundPackets.TryTake(out ServerPacket packet))
                    {
                        break;
                    }

                    processedCount++;
                    try
                    {
                        OnPacketReceived?.Invoke(packet);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(
                            new InvalidOperationException(
                                "A server packet could not be processed. Disconnecting to avoid continuing with corrupted state.",
                                ex));
                        HandleServerDisconnect("Client packet processing failed.");
                        break;
                    }
                }
            }
            finally
            {
                if (batchActive)
                {
                    OnPacketBatchCompleted?.Invoke();
                }
            }

            if (processedCount >= ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame &&
                !_inboundPackets.IsEmpty)
            {
                stoppedByCap = true;
            }

            // Остаток очереди и причина обрыва — единственный признак того, что
            // пакет уже пришёл, но до обработчика в этом кадре не добрался.
            PacketTelemetry.RecordQueueState(
                _inboundPackets.Count,
                _inboundPackets.PacketBytes,
                stoppedByBudget,
                stoppedByCap);
        }

        private void UpdateReconnect()
        {
            if (!_shouldAutoReconnect || Connection != null)
            {
                return;
            }

            _reconnectCountdown -= Time.unscaledDeltaTime;
            int secsRemaining = Mathf.CeilToInt(_reconnectCountdown);
            string status = secsRemaining > 0
                ? _loc.Get("network.reconnect.retry", secsRemaining)
                : _loc.Get("network.connecting");
            if (status != _reconnectStatus)
            {
                _reconnectStatus = status;
                OnReconnectStatusChanged?.Invoke(status);
            }

            if (_reconnectCountdown <= 0f)
            {
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                Connect();
            }
        }

        public void Connect(bool oldClient = false)
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                if (Connection.ConnectionStatus == ConnectionStatus.Connected)
                {
                    SendClientHelloIfNeeded();
                }

                return;
            }

            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;

                // DummyConnection — синглтон Bootstrap и переиспользуется на
                // следующем подключении; Dispose закрыл бы его состояние мира
                // навсегда. Освобождается только одноразовый сокетный транспорт.
                if (!ReferenceEquals(Connection, _dummyConnection))
                {
                    (Connection as IDisposable)?.Dispose();
                }

                Connection = null;
            }

            _useOldClient = oldClient;
            _isConnectionAttemptPending = true;
            _isWorldStartupPending = true;
            _clientHelloSent = false;
            Connection = _transportFactory.Create();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();

            _reconnectStatus = _loc.Get("network.connecting");
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
        }

        public void Disconnect()
        {
            _isConnectionAttemptPending = false;
            _isWorldStartupPending = false;
            _shouldAutoReconnect = false;
            _restartWorldOnConnect = false;
            _clientHelloSent = false;
            if (Connection == null)
            {
                return;
            }

            _inboundPackets.BeginTeardown();
            try
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                Connection.Disconnect();
                Connection = null;

                ClearPendingPackets();

            }
            finally
            {
                _inboundPackets.EndTeardown();
            }
        }

        public void TriggerDisconnect(string reason)
        {
            if (Connection is IOfflineConnection offline)
            {
                offline.TriggerDisconnect(reason);
                return;
            }

            HandleServerDisconnect(reason);
        }

        public void TriggerReconnect(string reason)
        {
            if (Connection is IOfflineConnection offline)
            {
                offline.TriggerReconnect(reason);
                return;
            }

            Disconnect();
        }

        public void Send(ClientPacket packet)
        {
            Connection?.SendAsync(packet);
        }

        public void MarkWorldReady()
        {
            if (!IsConnected)
            {
                throw new ConnectionStartupInterruptedException();
            }

            _isWorldStartupPending = false;
            _shouldAutoReconnect = false;
            _restartWorldOnConnect = false;
            _reconnectBackoff.Reset();
            _reconnectCountdown = 0f;
            _reconnectStatus = string.Empty;
            OnReconnectHidden?.Invoke();
        }

        public void HandleServerDisconnect(string reason)
        {
            _shouldAutoReconnect = false;
            _disconnectReason = reason;
            Disconnect();
            Debug.LogWarning($"[ConnectionManager] Server disconnected the client: {reason}");
            OnDisconnectReason?.Invoke(reason);
            OnTransportDisconnected?.Invoke();
        }

        public void HandleServerReconnect()
        {
            Disconnect();
            _restartWorldOnConnect = true;
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
            OnTransportDisconnected?.Invoke();
        }
        private void OnConnected()
        {
            _isConnectionAttemptPending = false;
            _operations.Run("complete_connection", CompleteConnectionAsync);
        }

        private async UniTask CompleteConnectionAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            CancellationToken cancellationToken = linkedCancellation.Token;

            if (_restartWorldOnConnect)
            {
                bool alreadyInTargetScene = string.Equals(
                    _sceneNavigator.CurrentSceneName,
                    ProjectRuntimeContracts.SceneNames.MainGame,
                    StringComparison.Ordinal);
                // Always clear the flag here so a missing reload (e.g. re-entry)
                // doesn't keep us pinned in restart mode for the next connect.
                _restartWorldOnConnect = false;
                if (!alreadyInTargetScene)
                {
                    try
                    {
                        await _sceneNavigator.TransitionAsync(
                            ProjectRuntimeContracts.SceneNames.MainGame,
                            cancellationToken);
                    }
                    catch (ConnectionStartupInterruptedException) when (_shouldAutoReconnect)
                    {
                        return;
                    }
                    catch (TimeoutException) when (_shouldAutoReconnect)
                    {
                        return;
                    }
                }
                else
                {
                    Debug.Log(
                        "[Connection] Restart-on-connect suppressed: already inside MainGame; skipping redundant transition.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            _shouldAutoReconnect = false;
            _reconnectBackoff.Reset();
            _reconnectStatus = string.Empty;
            OnReconnectHidden?.Invoke();

            SendClientHelloIfNeeded();

            // On a reconnect, startup calls Connect() after packet consumers
            // are initialized. That call sends ClientHello while this
            // transition is waiting for WorldInit; the guard makes this
            // completion path safe to call it again after readiness.
        }

        private void SendClientHelloIfNeeded()
        {
            if (_clientHelloSent || Connection == null || !IsConnected)
            {
                return;
            }

            _clientHelloSent = true;
            int version = _useOldClient ? 0 : ProjectRuntimeContracts.Networking.ClientVersion;
            string token = _tokens.Load();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");
            Connection?.SendAsync(new ClientPacket(
                (uint)DateTimeOffset.UtcNow.Ticks,
                new ClientHelloPacket(
                    version,
                    GetClientOperatingSystem(),
                    Environment.OSVersion.Version.Major,
                    SystemInfo.deviceUniqueIdentifier,
                    token)));
        }

        private static string GetClientOperatingSystem() =>
            Application.platform switch
            {
                RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "macOS",
                RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "Windows",
                _ => Application.platform.ToString(),
            };

        private void OnDisconnected()
        {
            if (_inboundPackets.IsTearingDown)
            {
                // Явный teardown (Disconnect/HandleServer*) уже выполнил очистку.
                return;
            }

            ClearPendingPackets();

            // Detach before forgetting the transport. DummyConnection is reused
            // on the next attempt; retaining these handlers delivers every
            // packet and lifecycle event multiple times after reconnecting.
            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                Connection = null;
            }

            if (_isConnectionAttemptPending || _isWorldStartupPending)
            {
                _isConnectionAttemptPending = false;
                _shouldAutoReconnect = true;
                _restartWorldOnConnect = true;
            }

            if (_shouldAutoReconnect)
            {
                // Сокетный транспорт может оборваться в любой момент. Забываем
                // мёртвое соединение, чтобы UpdateReconnect создал новое.
                Connection = null;
                _restartWorldOnConnect = true;
                _reconnectBackoff.RecordFailure();
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
                OnReconnectStatusChanged?.Invoke(_reconnectStatus);
                OnTransportDisconnected?.Invoke();
                return;
            }

            Connection = null;
            _disconnectReason = _loc.Get("network.error.connection_lost");
            OnDisconnectReason?.Invoke(_disconnectReason);
            OnTransportDisconnected?.Invoke();
        }

        private void ClearPendingPackets()
        {
            int discardedCount = _inboundPackets.Clear();

            if (discardedCount > 0)
            {
                Debug.LogWarning(
                    $"[ConnectionManager] Discarded {discardedCount} stale packet(s) after disconnect.");
            }
        }

        private void OnReceived(ServerPacket obj)
        {
            if (_inboundPackets.IsTearingDown || obj.Payload is null)
            {
                return;
            }

            _inboundPackets.Enqueue(obj);
        }

    }
}
