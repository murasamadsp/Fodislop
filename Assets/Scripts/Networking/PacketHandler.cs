#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using Fodinae.Networking.Processors;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Fodinae.Networking
{
    /// <summary>
    /// Pure packet dispatcher: binds packet types to processors and owns the
    /// subscription lifetime against <see cref="INetworkService"/>. It holds no
    /// UI, no scene managers and no player state — every packet is routed to a
    /// processor that updates a model, an event gateway or a domain service.
    /// </summary>
    public partial class PacketHandler : MonoBehaviour
    {
        private bool _isInitialized;
        private bool _isSubscribed;
        private INetworkService? _subscribedNetworkService;

        [Inject]
        private ChatProcessor _chat = null!;
        [Inject]
        private AudioPacketProcessor _audio = null!;
        [Inject]
        private PlayerInfoProcessor _playerInfo = null!;
        [Inject]
        private MapRegionProcessor _mapRegion = null!;
        [Inject]
        private MissionProcessor _mission = null!;
        [Inject]
        private BuildingProcessor _building = null!;
        [Inject]
        private ConnectionProcessor _connection = null!;
        [Inject]
        private MissionArrowProcessor _missionArrow = null!;
        [Inject]
        private WindowPacketProcessor _windowProcessor = null!;
        [Inject]
        private PlayerStatsProcessor _playerStats = null!;
        [Inject]
        private StatusProcessor _status = null!;
        [Inject]
        private InventoryProcessor _inventory = null!;
        [Inject]
        private ClanProcessor _clan = null!;
        [Inject]
        private WorldInitProcessor _worldInit = null!;
        [Inject]
        private AuthTokenProcessor _authToken = null!;
        [Inject]
        private INetworkService _networkService = null!;

        protected virtual void Awake()
        {
            TryInitialize();
        }

        protected void Start()
        {
            TryInitialize();
        }

        public void EnsureInitialized()
        {
            if (!TryInitialize() || !_isSubscribed)
            {
                throw new InvalidOperationException(
                    "PacketHandler dependencies were not injected before startup completed.");
            }
        }

        private bool TryInitialize()
        {
            if (_networkService == null)
            {
                return false;
            }

            if (!_isInitialized)
            {
                _isInitialized = true;
            }

            TrySubscribeToNetworkService();
            return true;
        }

        private readonly List<Action> _unsubscribers = new();

        // Protocol packets may be value types, so this helper must remain unconstrained.
        private void Subscribe<T>(Action<T> handler)
        {
            INetworkService networkService = _networkService;
            networkService.Subscribe(handler);
            _unsubscribers.Add(() => networkService.Unsubscribe(handler));
        }

        private void TrySubscribeToNetworkService()
        {
            if (_networkService == null)
            {
                return;
            }

            // Repeated initialization of the same scene must be a no-op. If the
            // dispatcher instance changed (domain reload or scope rebuild), detach
            // from the old dispatcher before binding the new one; otherwise the old
            // graph keeps receiving packets after the scene has been replaced.
            if (_isSubscribed && ReferenceEquals(_subscribedNetworkService, _networkService))
            {
                return;
            }

            if (_isSubscribed)
            {
                UnsubscribePacketSubscriptions();
            }

            Subscribe<WorldInitPacket>(_worldInit.Process);
            Subscribe<RobotInfoPacket>(_playerInfo.Process);
            Subscribe<PlayerInfoPacket>(_playerInfo.Process);
            Subscribe<MovementSpeedPacket>(_playerInfo.Process);
            Subscribe<OpenWindowPacket>(_windowProcessor.Process);
            Subscribe<CloseWindowPacket>(_windowProcessor.Process);
            Subscribe<RobotPositionPacket>(_playerInfo.Process);
            Subscribe<MapRegionPacket>(_mapRegion.Process);
            Subscribe<PackPacket>(_building.Process);
            Subscribe<RemovePackPacket>(_building.Process);

            Subscribe<LevelPacket>(_playerStats.Process);
            Subscribe<HealthPacket>(_playerStats.Process);
            Subscribe<CurrencyPacket>(_playerStats.Process);
            Subscribe<GeologyPacket>(_playerStats.Process);
            Subscribe<BasketPacket>(_playerStats.Process);
            Subscribe<MaxDepthPacket>(_playerStats.Process);

            Subscribe<AutoMineStatePacket>(_playerInfo.Process);
            Subscribe<AggressionStatePacket>(_playerInfo.Process);
            Subscribe<SkillProgressPacket>(_playerStats.Process);
            Subscribe<DailyBonusStatePacket>(_playerStats.Process);
            Subscribe<TeleportPacket>(_playerInfo.Process);
            Subscribe<ChatMessageListPacket>(_chat.Process);
            Subscribe<LocalChatMessagePacket>(_chat.Process);
            Subscribe<ChatMutePacket>(_chat.Process);
            Subscribe<ChatListPacket>(_chat.Process);

            Subscribe<OnlinePacket>(_status.Process);
            Subscribe<PingPacket>(_status.Process);
            Subscribe<OutdatedClientPacket>(_status.Process);
            Subscribe<AudioPacket>(_audio.Process);
            Subscribe<InventoryPacket>(_inventory.Process);
            Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(_inventory.Process);
            Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(_inventory.Process);
            Subscribe<AddStatusLinePacket>(_status.Process);
            Subscribe<ClearStatusLinePacket>(_status.Process);
            Subscribe<ClearStatusPacket>(_status.Process);
            Subscribe<ModalWindowPacket>(_windowProcessor.Process);
            Subscribe<ShowClanPacket>(_clan.Process);
            Subscribe<HideClanPacket>(_clan.Process);
            Subscribe<MissionInitPacket>(_mission.Process);
            Subscribe<MissionProgressPacket>(_mission.Process);
            Subscribe<DisconnectPacket>(_connection.Process);
            Subscribe<ReconnectPacket>(_connection.Process);
            Subscribe<AuthTokenPacket>(_authToken.Process);
            Subscribe<OpenURLPacket>(packet => Application.OpenURL(packet.URL));
            Subscribe<MissionArrowPacket>(_missionArrow.Process);

            _subscribedNetworkService = _networkService;
            _isSubscribed = true;
        }

        /// <summary>
        /// Detaches every packet subscription. Idempotent.
        /// </summary>
        /// <remarks>
        /// Split out of <c>OnDestroy</c> so it can be called BEFORE the game
        /// scene starts unloading, which is the only point at which it actually
        /// prevents anything. The connection lives in the Bootstrap scope and
        /// keeps draining packets across the transition by design, while
        /// OnDestroy runs *inside* the unload in an order Unity does not define.
        /// OnDestroy still calls this as a backstop.
        /// </remarks>
        public void Shutdown()
        {
            UnsubscribeAll();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeAll();
        }

        private void UnsubscribeAll()
        {
            if (!_isInitialized || !_isSubscribed)
            {
                return;
            }

            UnsubscribePacketSubscriptions();
        }

        private void UnsubscribePacketSubscriptions()
        {
            if (_networkService != null)
            {
                foreach (Action unsubscribe in _unsubscribers)
                {
                    unsubscribe();
                }
            }

            _unsubscribers.Clear();
            _isSubscribed = false;
            _subscribedNetworkService = null;
        }
    }
}
