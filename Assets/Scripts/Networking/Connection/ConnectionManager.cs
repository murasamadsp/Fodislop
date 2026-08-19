#nullable enable

using System;
using System.Collections.Concurrent;
using System.Net;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking.Auth;
using Fodinae.UI;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using UnityEngine;
using VContainer;

namespace Fodinae.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour, IConnectionService
    {
        public static ConnectionManager? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Instance = null;
        }

        // Бюджет на обработку входящих пакетов — доля времени КАДРА, а не стены часов.
        // Пропорция к deltaTime масштабирует пропускную способность с частотой кадров
        // (независимость от 30 vs 144 FPS) и ограничивает долю CPU, но при этом
        // всплеск пакетов (мировые текстуры при входе в мир) дренится за несколько
        // кадров, а не тянется секунду, как с накоплением 2% реального времени.
        private const float PacketDrainBudgetFractionOfFrame = 0.33f;
        private const float PacketDrainBudgetMaximumSeconds = 0.01f;

        public IServerConnection? Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected;
        public bool IsOffline => Connection is IOfflineConnection;
        private bool _useOldClient;
        public event Action<ServerPacket>? OnPacketReceived;

        private readonly ConcurrentQueue<ServerPacket> _packetQueue = new();
        private readonly ReconnectBackoff _reconnectBackoff = new();

        // Bootstrap-уровневые зависимости инжектятся VContainer напрямую. World-уровневые
        // (GameManager, MapManager, IWorldDataStorage) живут в дочернем scope сессии и
        // пересоздаются при каждом входе в мир — ConnectionManager переживает их, поэтому
        // обращение к ним идёт через ISessionContainer (текущий контейнер сессии).
        //
        // INetworkService НЕ инжектится: NetworkService сам инжектит IConnectionService,
        // и статическая связь дала бы VContainer циклическую зависимость (TypeAnalyzer
        // падает при сборке графа). Ленивый резолв в OnConnected разрывает цикл.
        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private ISessionContainer _session = null!;

        private bool _shouldAutoReconnect;
        private float _reconnectCountdown;
        private string _reconnectStatus = string.Empty;
        private bool _tearingDown;

        // НУЖЕН: сохраняет причину серверного дисконнекта — используется при реконнекте
        // и для диагностики в ReconnectUI. НЕ УДАЛЯТЬ (см. HandleServerDisconnect).
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052", Justification = "Хранит причину дисконнекта для реконнект-статуса")]
        private string _disconnectReason = string.Empty;

        protected void Awake()
        {
            Instance = this;
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

        /// <summary>
        /// Разбирает очередь входящих пакетов в рамках бюджета на кадр — доля
        /// <see cref="PacketDrainBudgetFractionOfFrame"/> от времени кадра, но не более
        /// <see cref="PacketDrainBudgetMaximumSeconds"/>. Батч за кадр дополнительно
        /// ограничен <see cref="ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame"/>,
        /// чтобы единичный всплеск не вешал кадр.
        /// </summary>
        private void DrainPacketQueue()
        {
            float budgetSeconds = Mathf.Min(
                Time.unscaledDeltaTime * PacketDrainBudgetFractionOfFrame,
                PacketDrainBudgetMaximumSeconds);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int processedCount = 0;
            while (_packetQueue.TryDequeue(out ServerPacket packet))
            {
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
                    TriggerDisconnect("Client packet processing failed.");
                    break;
                }

                if (processedCount >= ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame)
                {
                    break;
                }

                float elapsedMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (elapsedMs >= budgetSeconds * 1000f)
                {
                    break;
                }
            }
        }

        private void UpdateReconnect()
        {
            if (!_shouldAutoReconnect || Connection != null)
            {
                return;
            }

            _reconnectCountdown -= Time.deltaTime;
            int secsRemaining = Mathf.CeilToInt(_reconnectCountdown);
            string status = secsRemaining > 0
                ? $"Попробуем ещё раз через {secsRemaining}с..."
                : "Подключение...";
            if (status != _reconnectStatus)
            {
                _reconnectStatus = status;
                ReconnectUI.Instance?.SetStatus(status);
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
                return;
            }

            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                (Connection as IDisposable)?.Dispose();
                Connection = null;
            }

            _useOldClient = oldClient;
            _session.TryResolve<GameManager>()?.SetState(Game.Managers.GameState.Connecting);

            Connection = CreateConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();

            _reconnectStatus = "Подключение...";
            ReconnectUI.Instance?.SetStatus(_reconnectStatus);
        }

        /// <summary>
        /// Выбирает транспорт: реальный Darkar25 <see cref="TcpConnection"/> из
        /// конфига, либо офлайн-заглушку <see cref="DummyConnection"/> для
        /// локального теста без сервера.
        /// </summary>
        private IServerConnection CreateConnection()
        {
            // Config может быть ещё не загружен (ClientConfigManager грузит его в Start).
            ClientConfig? config = _clientConfigManager.Config;
            if (config == null)
            {
                Debug.LogWarning(
                    "[Connection] Client config is not initialized yet; using DummyConnection (offline stub).");
                return new DummyConnection(_session);
            }

            if (ConnectionTransportConfig.SelectTransport(config.UseDummyConnection) == ConnectionTransportKind.Dummy)
            {
                Debug.Log(
                    "[Connection] Transport: DummyConnection (offline stub). Set UseDummyConnection=false in client config for the real server.");
                return new DummyConnection(_session);
            }

            if (!ConnectionTransportConfig.TryResolveEndpoint(
                    config.ServerHost,
                    config.ServerPort,
                    out IPAddress address,
                    out int port))
            {
                throw new InvalidOperationException(
                    $"[Connection] Invalid server endpoint '{config.ServerHost}:{config.ServerPort}' in client config. " +
                    "Expected a valid host/IP and a port in [1, 65535].");
            }

            Debug.Log($"[Connection] Transport: TcpConnection {address}:{port} (Darkar25 MinesServerNetworking).");
            return new TcpConnection(address, port);
        }

        public void Disconnect()
        {
            if (Connection == null)
            {
                return;
            }

            _tearingDown = true;
            try
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                Connection.Disconnect();
                Connection = null;

                ClearPendingPackets();

                _session.TryResolve<MapManager>()?.ResetWorldState();
                _session.TryResolve<IWorldDataStorage>()?.Dispose();
            }
            finally
            {
                _tearingDown = false;
            }
        }

        public void TriggerDisconnect(string reason)
        {
            if (Connection is IOfflineConnection offline)
            {
                offline.TriggerDisconnect(reason);
                return;
            }

            Disconnect();
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

        public void HandleServerDisconnect(string reason)
        {
            _shouldAutoReconnect = false;
            _disconnectReason = reason;
            Disconnect();
            _session.TryResolve<GameManager>()?.DeauthorizeUI();
            ReconnectUI.Instance?.ShowDisconnectReason(reason);
        }

        public void HandleServerReconnect()
        {
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
            Disconnect();
            _session.TryResolve<GameManager>()?.SetState(Game.Managers.GameState.Disconnected);
            ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
        }

        public void StartManualReconnect()
        {
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
        }

        private void OnConnected()
        {
            _shouldAutoReconnect = false;
            _reconnectBackoff.Reset();
            _reconnectStatus = string.Empty;
            ReconnectUI.Instance?.Hide();

            int version = _useOldClient ? 0 : 1;
            string token = AuthTokenManager.LoadToken();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");
            INetworkService networkService = _session.Resolve<INetworkService>();
            networkService.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", token));

            networkService.Send(new OpenHelpClickPacket());
        }

        private void OnDisconnected()
        {
            if (_tearingDown)
            {
                // Явный teardown (Disconnect/HandleServer*) уже выполнил очистку.
                return;
            }

            ClearPendingPackets();

            _session.TryResolve<MapManager>()?.ResetWorldState();
            _session.TryResolve<IWorldDataStorage>()?.Dispose();
            GameManager? gameManager = _session.TryResolve<GameManager>();
            gameManager?.DeauthorizeUI();

            if (_shouldAutoReconnect)
            {
                // Сокетный транспорт может оборваться в любой момент. Забываем
                // мёртвое соединение, чтобы UpdateReconnect создал новое.
                Connection = null;
                _reconnectBackoff.RecordFailure();
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
                gameManager?.SetState(Game.Managers.GameState.Disconnected);
                ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
            }
        }

        private void ClearPendingPackets()
        {
            int discardedCount = 0;
            while (_packetQueue.TryDequeue(out _))
            {
                discardedCount++;
            }

            if (discardedCount > 0)
            {
                Debug.LogWarning(
                    $"[ConnectionManager] Discarded {discardedCount} stale packet(s) after disconnect.");
            }
        }

        private void OnReceived(ServerPacket obj)
        {
            if (_tearingDown)
            {
                return;
            }

            if (obj != null)
            {
                _packetQueue.Enqueue(obj);
            }
        }
    }
}
