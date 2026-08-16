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
        public static NetworkService? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Instance = null;
        }

        [Inject]
        private IConnectionService _connectionService = null!;
        private IConnectionService? _subscribedConnection;

        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();
        private readonly Dictionary<Type, Subscription[]> _subscriberSnapshots = new();
        private readonly object _subscribersLock = new();
        private bool _connectionSubscribed;

        public bool IsConnectionSubscriptionEstablished => _connectionSubscribed;

        protected void Awake()
        {
            Instance = this;
        }

        protected void OnEnable()
        {
            EnsureConnectionSubscription();
        }

        protected void OnDisable()
        {
            UnsubscribeFromConnection();
        }

        protected void OnDestroy()
        {
            UnsubscribeFromConnection();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Binds the packet stream after VContainer injection. Unity may call
        /// OnEnable before [Inject] has populated the connection field.
        /// </summary>
        public void EnsureConnectionSubscription()
        {
            if (ServiceLocator.IsInitialized)
            {
                _connectionService = ServiceLocator.Resolve<IConnectionService>() ??
                    throw new InvalidOperationException(
                        "NetworkService requires IConnectionService in the active resolver.");
            }

            if (_subscribedConnection != null)
            {
                _subscribedConnection.OnPacketReceived -= OnPacketReceived;
                _subscribedConnection = null;
            }

            if (_connectionService == null)
            {
                _connectionSubscribed = false;
                return;
            }

            // Rebind even when the local flag says "subscribed". During an
            // editor/domain reload the ConnectionManager instance can be
            // replaced while this component survives; the old boolean then
            // describes a dead connection event source.
            _connectionService.OnPacketReceived -= OnPacketReceived;
            _connectionService.OnPacketReceived += OnPacketReceived;
            _subscribedConnection = _connectionService;
            _connectionSubscribed = true;
        }

        private void UnsubscribeFromConnection()
        {
            if (_subscribedConnection == null)
            {
                _connectionSubscribed = false;
                return;
            }

            _subscribedConnection.OnPacketReceived -= OnPacketReceived;
            _subscribedConnection = null;
            _connectionSubscribed = false;
        }

        public bool IsConnected
        {
            get
            {
                return _connectionService != null && _connectionService.IsConnected;
            }
        }

        private PlayerMovementController? _cachedPlayerController;

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
            var connectionService = Fodinae.Core.ServiceLocator.Resolve<IConnectionService>()!;
            var timestamp = (uint)DateTimeOffset.UtcNow.Ticks;
            connectionService.Send(new ClientPacket(timestamp, packet));
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
                _subscriberSnapshots[type] = handlers.ToArray();
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
                    _subscriberSnapshots[type] = handlers.ToArray();
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
            else
            {
                Dispatch(payload);
            }
        }

        private void Dispatch(object packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetType = packet.GetType();

            Subscription[] handlers;
            lock (_subscribersLock)
            {
                if (!_subscriberSnapshots.TryGetValue(packetType, out var snapshot))
                {
                    return;
                }

                handlers = snapshot;
            }

            for (int i = handlers.Length - 1; i >= 0; i--)
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
            public Delegate OriginalHandler { get; set; } = null!;
            public Action<object> Wrapper { get; set; } = null!;
        }
    }
}
