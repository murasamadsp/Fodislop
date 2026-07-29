#nullable enable

using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Auth;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using UnityEngine;
using VContainer;

namespace Fodinae.Scripts.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour, IConnectionService
    {
        public static ConnectionManager Instance { get; private set; }

        public IServerConnection Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected;
        private bool _useOldClient;
        public event Action<ServerPacket>? OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        private bool _shouldAutoReconnect;
        private float _reconnectCountdown;
        private const float ReconnectInterval = 20f;
        private string _reconnectStatus = string.Empty;
        private bool _serverInitiatedDisconnect;
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
            Fodinae.Scripts.Core.ServiceLocator.Resolve<GameManager>()?.SetState(Game.Managers.GameState.Connecting);

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

            (ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage)?.Dispose();
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
            NetworkService.Instance?.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", token));

            NetworkService.Instance?.Send(new OpenHelpClickPacket());
        }

        private void OnDisconnected()
        {
            ServiceLocator.Resolve<GameManager>()?.DeauthorizeUI();

            if (_shouldAutoReconnect && !_serverInitiatedDisconnect)
            {
                _reconnectCountdown = ReconnectInterval;
                _reconnectStatus = $"Попробуем ещё раз через {Mathf.CeilToInt(_reconnectCountdown)}с...";
                ServiceLocator.Resolve<GameManager>()?.SetState(Game.Managers.GameState.Disconnected);
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
