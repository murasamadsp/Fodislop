#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Compression;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Fodinae.Networking
{
    public class NetworkService : MonoBehaviour, INetworkService
    {
        public static NetworkService Instance { get; private set; }

        [Inject]
        private IConnectionService _connectionService = null!;

        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();
        private readonly object _subscribersLock = new();

        protected void Awake()
        {
            Instance = this;
        }

        protected void OnEnable()
        {
            if (_connectionService != null)
            {
                _connectionService.OnPacketReceived -= OnPacketReceived;
                _connectionService.OnPacketReceived += OnPacketReceived;
            }
        }

        protected void OnDisable()
        {
            if (_connectionService != null)
            {
                _connectionService.OnPacketReceived -= OnPacketReceived;
            }
        }

        public bool IsConnected
        {
            get
            {
                return _connectionService != null && _connectionService.IsConnected;
            }
        }

        private PlayerMovementController _cachedPlayerController;

        public void SendAction(IActionClientPacket action)
        {
            if (!IsConnected)
            {
                return;
            }

            if (_cachedPlayerController == null)
            {
                _cachedPlayerController = PlayerMovementController.LocalPlayer;
            }

            if (_cachedPlayerController == null)
            {
                Debug.LogError("[NetworkService] Cannot send action: PlayerMovementController not found.");
                return;
            }

            Vector2Int pos = _cachedPlayerController.Position;
            ushort serverX = (ushort)pos.x;
            ushort serverY = (ushort)pos.y;

            Send(new ActionClientPacket(serverX, serverY, action));
        }

        public void Send(IRootClientPacket packet)
        {
            var connection = Fodinae.Core.ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
            if (connection == null || connection.Connection == null)
            {
                return;
            }

            var timestamp = (uint)DateTimeOffset.UtcNow.Ticks;
            connection.Connection.SendAsync(new ClientPacket(timestamp, packet));
        }

        void INetworkService.Send(IRootClientPacket packet) => Send(packet);

        public void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            lock (_subscribersLock)
            {
                if (!_subscribers.TryGetValue(type, out var handlers))
                {
                    handlers = new List<Subscription>();
                    _subscribers[type] = handlers;
                }

                bool alreadySubscribed = false;
                for (int i = 0; i < handlers.Count; i++)
                {
                    if (handlers[i].OriginalHandler == (Delegate)handler)
                    {
                        alreadySubscribed = true;
                        break;
                    }
                }

                if (alreadySubscribed)
                {
                    return;
                }

                handlers.Add(new Subscription
                {
                    OriginalHandler = handler,
                    Wrapper = obj => handler((T)obj),
                });
            }
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            lock (_subscribersLock)
            {
                if (_subscribers.TryGetValue(type, out var handlers))
                {
                    handlers.RemoveAll(s => s.OriginalHandler == (Delegate)handler);
                }
            }
        }

        private void OnPacketReceived(ServerPacket packet)
        {
            var payload = packet.Payload;
            if (payload == null)
            {
                Debug.LogWarning("[NetworkService] Received ServerPacket with null Payload");
                return;
            }

            while (payload is LzmaPacket lzma)
            {
                payload = lzma.Payload;
            }

            while (payload is LZ4Packet lz4)
            {
                payload = lz4.Payload;
            }

            if (payload is HBPacket hbPacket && hbPacket.Payload != null)
            {
                foreach (var innerPacket in hbPacket.Payload)
                {
                    Dispatch(innerPacket);
                }
            }

            Dispatch(payload);
        }

        private void Dispatch(object packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetType = packet.GetType();

            List<Subscription> handlers;
            lock (_subscribersLock)
            {
                if (!_subscribers.TryGetValue(packetType, out var h))
                {
                    return;
                }

                handlers = new List<Subscription>(h);
            }

            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                try
                {
                    handlers[i].Wrapper(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetworkService] Error dispatching packet {packetType.Name} to subscriber: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }


        private class Subscription
        {
            public Delegate OriginalHandler { get; set; }
            public Action<object> Wrapper { get; set; }
        }
    }
}
