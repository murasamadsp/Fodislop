#nullable enable

using System.Collections.Generic;
using System.Linq;
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
        public bool IsInputBlocked => _windowProcessor != null && (_windowProcessor.HasOpenWindows || _windowProcessor.IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen);
        public string? TopWindowTag => _windowProcessor != null ? _windowProcessor.TopWindowTag : null;

        private static readonly WorldInitProcessor WorldInit = new();
        private static readonly RobotInfoProcessor RobotInfo = new();
        private static readonly MapRegionProcessor MapRegion = new();
        private static readonly AudioPacketProcessor Audio = new();
        private static readonly PlayerInfoProcessor PlayerInfo = new();
        private static readonly PlayerStatsProcessor PlayerStats = new();
        private static readonly PlayerStateProcessor PlayerState = new();
        private static readonly RobotPositionProcessor RobotPosition = new();
        private static readonly ChatProcessor Chat = new();
        private static readonly StatusProcessor Status = new();
        private static readonly InventoryProcessor Inventory = new();
        private static readonly ClanProcessor Clan = new();
        private static readonly MissionProcessor Mission = new();
        private static readonly PackProcessor Pack = new();
        private static readonly ConnectionProcessor Connection = new();
        private static readonly OpenURLProcessor OpenURL = new();
        private static readonly ClientConfigProcessor ClientConfig = new();
        private static readonly MissionArrowProcessor MissionArrow = new();
        private readonly WindowPacketProcessor _windowProcessor = new();
        private bool _isInitialized;
        private bool _isSubscribed;

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
        private MapStorage? MapStorage => _mapStorageInterface as MapStorage;

        protected virtual void Awake()
        {
            if (_mapDataProvider == null)
            {
                Debug.LogError("[PacketHandler] FATAL: IMapDataProvider is not injected — PacketHandler cannot function. World will not render.");
                return;
            }

            var mapStorage = MapStorage;
            if (mapStorage == null)
            {
                Debug.LogError("[PacketHandler] FATAL: MapStorage not found at Awake — PacketHandler cannot function. World will not render.");
                return;
            }

            var modalWindowHandler = new ModalWindowHandler(_uiDocument);
            _windowProcessor.Initialize(_uiDocument, modalWindowHandler);

            TrySubscribeToNetworkService();

            if (_mapDataProvider is MapManager concreteMM)
            {
                concreteMM.OnWorldInitialized += OnWorldInitialized;
            }

            _isInitialized = true;
        }

        protected void Start()
        {
            TrySubscribeToNetworkService();
        }

        private void TrySubscribeToNetworkService()
        {
            if (_isSubscribed || _networkService == null)
            {
                return;
            }

            _networkService.Subscribe<WorldInitPacket>(WorldInit.Process);
            _networkService.Subscribe<RobotInfoPacket>(RobotInfo.Process);
            _networkService.Subscribe<PlayerInfoPacket>(PlayerInfo.Process);
            _networkService.Subscribe<MovementSpeedPacket>(PlayerInfo.Process);
            _networkService.Subscribe<OpenWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<CloseWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<RobotPositionPacket>(RobotPosition.Process);
            _networkService.Subscribe<MapRegionPacket>(MapRegion.Process);
            _networkService.Subscribe<PackPacket>(Pack.Process);
            _networkService.Subscribe<RemovePackPacket>(Pack.Process);

            _networkService.Subscribe<LevelPacket>(PlayerStats.Process);
            _networkService.Subscribe<HealthPacket>(PlayerStats.Process);
            _networkService.Subscribe<CurrencyPacket>(PlayerStats.Process);
            _networkService.Subscribe<GeologyPacket>(PlayerStats.Process);
            _networkService.Subscribe<BasketPacket>(PlayerStats.Process);
            _networkService.Subscribe<MaxDepthPacket>(PlayerStats.Process);

            _networkService.Subscribe<AutoMineStatePacket>(PlayerState.Process);
            _networkService.Subscribe<AggressionStatePacket>(PlayerState.Process);
            _networkService.Subscribe<SkillProgressPacket>(PlayerStats.Process);
            _networkService.Subscribe<DailyBonusStatePacket>(PlayerStats.Process);
            _networkService.Subscribe<TeleportPacket>(PlayerInfo.Process);
            _networkService.Subscribe<ChatMessageListPacket>(Chat.Process);
            _networkService.Subscribe<LocalChatMessagePacket>(Chat.Process);
            _networkService.Subscribe<ChatMutePacket>(Chat.Process);
            _networkService.Subscribe<ChatListPacket>(Chat.Process);

            _networkService.Subscribe<OnlinePacket>(Status.Process);
            _networkService.Subscribe<PingPacket>(Status.Process);
            _networkService.Subscribe<OutdatedClientPacket>(Status.Process);
            _networkService.Subscribe<AudioPacket>(Audio.Process);
            _networkService.Subscribe<InventoryPacket>(Inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
            _networkService.Subscribe<AddStatusLinePacket>(Status.Process);
            _networkService.Subscribe<ClearStatusLinePacket>(Status.Process);
            _networkService.Subscribe<ClearStatusPacket>(Status.Process);
            _networkService.Subscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
            _networkService.Subscribe<ShowClanPacket>(Clan.Process);
            _networkService.Subscribe<HideClanPacket>(Clan.Process);
            _networkService.Subscribe<MissionInitPacket>(Mission.Process);
            _networkService.Subscribe<MissionProgressPacket>(Mission.Process);
            _networkService.Subscribe<DisconnectPacket>(Connection.Process);
            _networkService.Subscribe<ReconnectPacket>(Connection.Process);
            _networkService.Subscribe<AuthTokenPacket>(HandleAuthTokenPacket);
            _networkService.Subscribe<OpenURLPacket>(OpenURL.Process);
            _networkService.Subscribe<ClientConfigPacket>(ClientConfig.Process);
            _networkService.Subscribe<MissionArrowPacket>(MissionArrow.Process);

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
                _networkService.Unsubscribe<WorldInitPacket>(WorldInit.Process);
                _networkService.Unsubscribe<RobotInfoPacket>(RobotInfo.Process);
                _networkService.Unsubscribe<PlayerInfoPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<MovementSpeedPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<OpenWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<CloseWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<RobotPositionPacket>(RobotPosition.Process);
                _networkService.Unsubscribe<MapRegionPacket>(MapRegion.Process);
                _networkService.Unsubscribe<PackPacket>(Pack.Process);
                _networkService.Unsubscribe<RemovePackPacket>(Pack.Process);
                _networkService.Unsubscribe<SkillProgressPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<AutoMineStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<AggressionStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<ChatMessageListPacket>(Chat.Process);
                _networkService.Unsubscribe<LocalChatMessagePacket>(Chat.Process);
                _networkService.Unsubscribe<ChatMutePacket>(Chat.Process);
                _networkService.Unsubscribe<ChatListPacket>(Chat.Process);

                _networkService.Unsubscribe<LevelPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<HealthPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<CurrencyPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<GeologyPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<BasketPacket>(PlayerStats.Process);

                _networkService.Unsubscribe<OnlinePacket>(Status.Process);
                _networkService.Unsubscribe<PingPacket>(Status.Process);

                _networkService.Unsubscribe<OutdatedClientPacket>(Status.Process);
                _networkService.Unsubscribe<AudioPacket>(Audio.Process);
                _networkService.Unsubscribe<InventoryPacket>(Inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
                _networkService.Unsubscribe<DailyBonusStatePacket>(PlayerStats.Process);
                _networkService.Unsubscribe<TeleportPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<AddStatusLinePacket>(Status.Process);
                _networkService.Unsubscribe<ClearStatusLinePacket>(Status.Process);
                _networkService.Unsubscribe<ClearStatusPacket>(Status.Process);
                _networkService.Unsubscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
                _networkService.Unsubscribe<ShowClanPacket>(Clan.Process);
                _networkService.Unsubscribe<HideClanPacket>(Clan.Process);
                _networkService.Unsubscribe<MaxDepthPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<MissionInitPacket>(Mission.Process);
                _networkService.Unsubscribe<MissionProgressPacket>(Mission.Process);
                _networkService.Unsubscribe<DisconnectPacket>(Connection.Process);
                _networkService.Unsubscribe<ReconnectPacket>(Connection.Process);
                _networkService.Unsubscribe<AuthTokenPacket>(HandleAuthTokenPacket);
                _networkService.Unsubscribe<OpenURLPacket>(OpenURL.Process);
                _networkService.Unsubscribe<ClientConfigPacket>(ClientConfig.Process);
                _networkService.Unsubscribe<MissionArrowPacket>(MissionArrow.Process);
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
                gm.SetState(GameState.InGame);
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
