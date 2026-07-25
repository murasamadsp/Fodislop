using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Auth;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour
    {
        private static ConnectionManager _instance;
        public static ConnectionManager Instance => _instance;
        public static ConnectionManager InstanceIfExists => _instance;

        public IServerConnection Connection { get; private set; }
        private bool _useOldClient;
        public event Action<ServerPacket> OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        private bool _shouldAutoReconnect;
        private float _reconnectCountdown;
        private const float ReconnectInterval = 20f;
        private string _reconnectStatus = string.Empty;
        private bool _serverInitiatedDisconnect;
        private string _disconnectReason = string.Empty;

        protected void Awake()
        {
            Debug.Log("[ConnectionManager] Awake START");
            _instance = this;
            gameObject.AddComponent<PacketHandler>();
            Debug.Log("[ConnectionManager] Awake END");
        }

        protected void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            Disconnect();
        }

        protected void Update()
        {
            float startTime = Time.realtimeSinceStartup;
            while (_packetQueue.TryDequeue(out var packet))
            {
                try
                {
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ConnectionManager] Error processing packet: {ex.Message}\n{ex.StackTrace}");
                }

                if ((Time.realtimeSinceStartup - startTime) * 1000f > 10f)
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

            _useOldClient = oldClient;
            Game.Managers.GameManager.InstanceIfExists?.SetState(Game.Managers.GameState.Connecting);

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

            MapStorage.InstanceIfExists?.Dispose();
        }

        public void HandleServerDisconnect(string reason)
        {
            _shouldAutoReconnect = false;
            _serverInitiatedDisconnect = true;
            _disconnectReason = reason;
            Disconnect();
            Game.Managers.GameManager.InstanceIfExists?.DeauthorizeUI();
            ReconnectUI.Instance?.ShowDisconnectReason(reason);
        }

        public void HandleServerReconnect()
        {
            _serverInitiatedDisconnect = false;
            _shouldAutoReconnect = true;
            _reconnectCountdown = ReconnectInterval;
            _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
            Disconnect();
            Game.Managers.GameManager.InstanceIfExists?.SetState(Game.Managers.GameState.Disconnected);
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
            NetworkService.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", token));

            NetworkService.Send(new OpenHelpClickPacket());
        }

        private void OnDisconnected()
        {
            Game.Managers.GameManager.InstanceIfExists?.DeauthorizeUI();

            if (_shouldAutoReconnect && !_serverInitiatedDisconnect)
            {
                _reconnectCountdown = ReconnectInterval;
                _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
                Game.Managers.GameManager.InstanceIfExists?.SetState(Game.Managers.GameState.Disconnected);
                ReconnectUI.Instance?.ShowReconnecting(_reconnectStatus);
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
