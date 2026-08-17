#nullable enable

using System;
using Fodinae.Core;
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

        public IServerConnection? Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected;
        public bool IsOffline => Connection is IOfflineConnection;
        private bool _useOldClient;
        public event Action<ServerPacket>? OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        private bool _shouldAutoReconnect;
        private float _reconnectCountdown;
        private const float ReconnectInterval = 20f;
        private string _reconnectStatus = string.Empty;
        private bool _serverInitiatedDisconnect;

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
            float startTime = Time.realtimeSinceStartup;
            int processedCount = 0;
            while (_packetQueue.TryDequeue(out var packet))
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

                if (processedCount >= ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame ||
                    (Time.realtimeSinceStartup - startTime) * 1000f > 33f)
                {
                    break;
                }
            }

            if (_shouldAutoReconnect && Connection == null)
            {
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
                    Connect();
                    _reconnectCountdown = ReconnectInterval;
                }
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
            ServiceLocator.Resolve<GameManager>()?.SetState(Game.Managers.GameState.Connecting);

            Connection = new DummyConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();

            _reconnectStatus = "Подключение...";
            ReconnectUI.Instance?.SetStatus(_reconnectStatus);
        }

        public void Disconnect()
        {
            if (Connection == null)
            {
                return;
            }

            Connection.OnReceived -= OnReceived;
            Connection.OnConnected -= OnConnected;
            Connection.OnDisconnected -= OnDisconnected;
            Connection.Disconnect();
            Connection = null;

            ClearPendingPackets();

            ServiceLocator.Resolve<MapManager>()?.ResetWorldState();
            ServiceLocator.Resolve<IWorldDataStorage>()?.Dispose();
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
            _serverInitiatedDisconnect = true;
            _disconnectReason = reason;
            Disconnect();
            ServiceLocator.Resolve<GameManager>()?.DeauthorizeUI();
            ReconnectUI.Instance?.ShowDisconnectReason(reason);
        }

        public void HandleServerReconnect()
        {
            _serverInitiatedDisconnect = false;
            _shouldAutoReconnect = true;
            _reconnectCountdown = ReconnectInterval;
            _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
            Disconnect();
            ServiceLocator.Resolve<GameManager>()?.SetState(Game.Managers.GameState.Disconnected);
            ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
        }

        public void StartManualReconnect()
        {
            _serverInitiatedDisconnect = false;
            _shouldAutoReconnect = true;
            _reconnectCountdown = ReconnectInterval;
            ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
        }

        private void OnConnected()
        {
            _shouldAutoReconnect = false;
            _serverInitiatedDisconnect = false;
            _reconnectStatus = string.Empty;
            ReconnectUI.Instance?.Hide();

            int version = _useOldClient ? 0 : 1;
            string token = AuthTokenManager.LoadToken();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");
            var ns = ServiceLocator.Resolve<INetworkService>();
            ns?.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", token));

            ns?.Send(new OpenHelpClickPacket());
        }

        private void OnDisconnected()
        {
            ClearPendingPackets();

            ServiceLocator.Resolve<MapManager>()?.ResetWorldState();
            ServiceLocator.Resolve<IWorldDataStorage>()?.Dispose();
            GameManager? gameManager = ServiceLocator.Resolve<GameManager>();
            gameManager?.DeauthorizeUI();

            if (_shouldAutoReconnect && !_serverInitiatedDisconnect)
            {
                _reconnectCountdown = ReconnectInterval;
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
            if (obj != null)
            {
                _packetQueue.Enqueue(obj);
            }
        }
    }
}
