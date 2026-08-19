#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking.Processors;
using Fodinae.Player;
using Fodinae.UI;
using Fodinae.UI.Binding;
using Fodinae.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.Networking
{
    public partial class PacketHandler : MonoBehaviour, IInputBlocker
    {
        public bool IsInputBlocked => ChatInput.IsFocused || (_windowProcessor != null && (_windowProcessor.HasOpenWindows || _windowProcessor.IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen));
        public string? TopWindowTag => _windowProcessor != null ? _windowProcessor.TopWindowTag : null;

        private static readonly PlayerStateProcessor PlayerState = new();
        private static readonly OpenURLProcessor OpenURL = new();
        private bool _isInitialized;
        private bool _isSubscribed;

        [Inject]
        private ISessionContainer _session = null!;
        [Inject]
        private WorldInitProcessor _worldInit = null!;
        [Inject]
        private RobotInfoProcessor _robotInfo = null!;
        [Inject]
        private MapRegionProcessor _mapRegion = null!;
        [Inject]
        private AudioPacketProcessor _audio = null!;
        [Inject]
        private PlayerInfoProcessor _playerInfo = null!;
        [Inject]
        private RobotPositionProcessor _robotPosition = null!;
        [Inject]
        private ChatProcessor _chat = null!;
        [Inject]
        private MissionProcessor _mission = null!;
        [Inject]
        private PackProcessor _pack = null!;
        [Inject]
        private ConnectionProcessor _connection = null!;
        [Inject]
        private ClientConfigProcessor _clientConfig = null!;
        [Inject]
        private MissionArrowProcessor _missionArrow = null!;
        [Inject]
        private WindowPacketProcessor _windowProcessor = null!;

        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IWorldDataStorage _mapStorageInterface = null!;
        [Inject]
        private GameManager _gameManager = null!;
        [Inject]
        private IMapDataProvider _mapDataProvider = null!;
        [Inject]
        private UIDocument _uiDocument = null!;
        [Inject]
        private PlayerStatsProcessor _playerStats = null!;
        [Inject]
        private StatusProcessor _status = null!;
        [Inject]
        private InventoryProcessor _inventory = null!;
        [Inject]
        private ClanProcessor _clan = null!;
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
            if (_isInitialized)
            {
                TrySubscribeToNetworkService();
                return true;
            }

            if (_mapDataProvider == null || _mapStorageInterface == null ||
                _uiDocument == null || _networkService == null ||
                _windowProcessor == null || _session == null)
            {
                return false;
            }

            var modalWindowHandler = new ModalWindowHandler(_uiDocument);
            _windowProcessor.Initialize(_uiDocument, modalWindowHandler);

            TrySubscribeToNetworkService();

            if (_mapDataProvider is MapManager concreteMM)
            {
                concreteMM.OnWorldInitialized += OnWorldInitialized;
            }

            _isInitialized = true;
            return true;
        }

        private void TrySubscribeToNetworkService()
        {
            if (_networkService == null && _session != null)
            {
                _networkService = _session.TryResolve<INetworkService>() ??
                    throw new InvalidOperationException(
                        "PacketHandler requires INetworkService in the active resolver.");
            }

            if (_networkService == null)
            {
                return;
            }

            // NetworkService deduplicates handlers. Re-registering here is
            // intentional: after a domain reload the injected service can be a
            // new instance while PacketHandler's _isSubscribed flag survives.
            // The old guard would then leave the new dispatcher empty.
            _networkService.Subscribe<WorldInitPacket>(_worldInit.Process);
            _networkService.Subscribe<RobotInfoPacket>(_robotInfo.Process);
            _networkService.Subscribe<PlayerInfoPacket>(_playerInfo.Process);
            _networkService.Subscribe<MovementSpeedPacket>(_playerInfo.Process);
            _networkService.Subscribe<OpenWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<CloseWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<RobotPositionPacket>(_robotPosition.Process);
            _networkService.Subscribe<MapRegionPacket>(_mapRegion.Process);
            _networkService.Subscribe<PackPacket>(_pack.Process);
            _networkService.Subscribe<RemovePackPacket>(_pack.Process);

            _networkService.Subscribe<LevelPacket>(_playerStats.Process);
            _networkService.Subscribe<HealthPacket>(_playerStats.Process);
            _networkService.Subscribe<CurrencyPacket>(_playerStats.Process);
            _networkService.Subscribe<GeologyPacket>(_playerStats.Process);
            _networkService.Subscribe<BasketPacket>(_playerStats.Process);
            _networkService.Subscribe<MaxDepthPacket>(_playerStats.Process);

            _networkService.Subscribe<AutoMineStatePacket>(PlayerState.Process);
            _networkService.Subscribe<AggressionStatePacket>(PlayerState.Process);
            _networkService.Subscribe<SkillProgressPacket>(_playerStats.Process);
            _networkService.Subscribe<DailyBonusStatePacket>(_playerStats.Process);
            _networkService.Subscribe<TeleportPacket>(_playerInfo.Process);
            _networkService.Subscribe<ChatMessageListPacket>(_chat.Process);
            _networkService.Subscribe<LocalChatMessagePacket>(_chat.Process);
            _networkService.Subscribe<ChatMutePacket>(_chat.Process);
            _networkService.Subscribe<ChatListPacket>(_chat.Process);

            _networkService.Subscribe<OnlinePacket>(_status.Process);
            _networkService.Subscribe<PingPacket>(_status.Process);
            _networkService.Subscribe<OutdatedClientPacket>(_status.Process);
            _networkService.Subscribe<AudioPacket>(_audio.Process);
            _networkService.Subscribe<InventoryPacket>(_inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(_inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(_inventory.Process);
            _networkService.Subscribe<AddStatusLinePacket>(_status.Process);
            _networkService.Subscribe<ClearStatusLinePacket>(_status.Process);
            _networkService.Subscribe<ClearStatusPacket>(_status.Process);
            _networkService.Subscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
            _networkService.Subscribe<ShowClanPacket>(_clan.Process);
            _networkService.Subscribe<HideClanPacket>(_clan.Process);
            _networkService.Subscribe<MissionInitPacket>(_mission.Process);
            _networkService.Subscribe<MissionProgressPacket>(_mission.Process);
            _networkService.Subscribe<DisconnectPacket>(_connection.Process);
            _networkService.Subscribe<ReconnectPacket>(_connection.Process);
            _networkService.Subscribe<AuthTokenPacket>(HandleAuthTokenPacket);
            _networkService.Subscribe<OpenURLPacket>(OpenURL.Process);
            _networkService.Subscribe<ClientConfigPacket>(_clientConfig.Process);
            _networkService.Subscribe<MissionArrowPacket>(_missionArrow.Process);

            _isSubscribed = true;
        }

        protected virtual void OnDestroy()
        {
            if (!_isInitialized || !_isSubscribed)
            {
                return;
            }

            if (_networkService != null)
            {
                _networkService.Unsubscribe<WorldInitPacket>(_worldInit.Process);
                _networkService.Unsubscribe<RobotInfoPacket>(_robotInfo.Process);
                _networkService.Unsubscribe<PlayerInfoPacket>(_playerInfo.Process);
                _networkService.Unsubscribe<MovementSpeedPacket>(_playerInfo.Process);
                _networkService.Unsubscribe<OpenWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<CloseWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<RobotPositionPacket>(_robotPosition.Process);
                _networkService.Unsubscribe<MapRegionPacket>(_mapRegion.Process);
                _networkService.Unsubscribe<PackPacket>(_pack.Process);
                _networkService.Unsubscribe<RemovePackPacket>(_pack.Process);
                _networkService.Unsubscribe<SkillProgressPacket>(_playerStats.Process);
                _networkService.Unsubscribe<AutoMineStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<AggressionStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<ChatMessageListPacket>(_chat.Process);
                _networkService.Unsubscribe<LocalChatMessagePacket>(_chat.Process);
                _networkService.Unsubscribe<ChatMutePacket>(_chat.Process);
                _networkService.Unsubscribe<ChatListPacket>(_chat.Process);

                _networkService.Unsubscribe<LevelPacket>(_playerStats.Process);
                _networkService.Unsubscribe<HealthPacket>(_playerStats.Process);
                _networkService.Unsubscribe<CurrencyPacket>(_playerStats.Process);
                _networkService.Unsubscribe<GeologyPacket>(_playerStats.Process);
                _networkService.Unsubscribe<BasketPacket>(_playerStats.Process);

                _networkService.Unsubscribe<OnlinePacket>(_status.Process);
                _networkService.Unsubscribe<PingPacket>(_status.Process);

                _networkService.Unsubscribe<OutdatedClientPacket>(_status.Process);
                _networkService.Unsubscribe<AudioPacket>(_audio.Process);
                _networkService.Unsubscribe<InventoryPacket>(_inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(_inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(_inventory.Process);
                _networkService.Unsubscribe<DailyBonusStatePacket>(_playerStats.Process);
                _networkService.Unsubscribe<TeleportPacket>(_playerInfo.Process);
                _networkService.Unsubscribe<AddStatusLinePacket>(_status.Process);
                _networkService.Unsubscribe<ClearStatusLinePacket>(_status.Process);
                _networkService.Unsubscribe<ClearStatusPacket>(_status.Process);
                _networkService.Unsubscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
                _networkService.Unsubscribe<ShowClanPacket>(_clan.Process);
                _networkService.Unsubscribe<HideClanPacket>(_clan.Process);
                _networkService.Unsubscribe<MaxDepthPacket>(_playerStats.Process);
                _networkService.Unsubscribe<MissionInitPacket>(_mission.Process);
                _networkService.Unsubscribe<MissionProgressPacket>(_mission.Process);
                _networkService.Unsubscribe<DisconnectPacket>(_connection.Process);
                _networkService.Unsubscribe<ReconnectPacket>(_connection.Process);
                _networkService.Unsubscribe<AuthTokenPacket>(HandleAuthTokenPacket);
                _networkService.Unsubscribe<OpenURLPacket>(OpenURL.Process);
                _networkService.Unsubscribe<ClientConfigPacket>(_clientConfig.Process);
                _networkService.Unsubscribe<MissionArrowPacket>(_missionArrow.Process);
            }

            _isSubscribed = false;

            if (_windowProcessor != null)
            {
                _windowProcessor.Dispose();
            }

            if (_mapDataProvider is MapManager concreteMM)
            {
                concreteMM.OnWorldInitialized -= OnWorldInitialized;
            }
        }

        private void OnWorldInitialized()
        {
            var gm = _gameManager;
            if (gm != null)
            {
                gm.NotifyWorldLoaded();
            }
        }

        private void HandleAuthTokenPacket(AuthTokenPacket packet)
        {
            string newToken = packet.Token;
            if (string.IsNullOrEmpty(newToken))
            {
                Debug.LogError("[Auth] Received empty token from server");
                return;
            }

            Auth.AuthTokenManager.SaveToken(newToken);

            var gm = _gameManager;
            if (gm != null)
            {
                gm.AuthorizeUI();
            }
        }
    }
}
