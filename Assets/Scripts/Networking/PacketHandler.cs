#nullable enable

using System;
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
        public bool IsInputBlocked => ChatInput.IsFocused || (_windowProcessor != null && (_windowProcessor.HasOpenWindows || _windowProcessor.IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen));
        public string? TopWindowTag => _windowProcessor != null ? _windowProcessor.TopWindowTag : null;

        private static readonly PlayerStateProcessor PlayerState = new();
        private static readonly OpenURLProcessor OpenURL = new();
        private bool _isInitialized;
        private bool _isSubscribed;
        private bool _emptyAuthTokenWarningLogged;

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
        private BuildingProcessor _building = null!;
        [Inject]
        private ConnectionProcessor _connection = null!;
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

            if (_worldInit == null ||
                _robotInfo == null ||
                _mapRegion == null ||
                _audio == null ||
                _playerInfo == null ||
                _robotPosition == null ||
                _chat == null ||
                _mission == null ||
                _building == null ||
                _connection == null ||
                _missionArrow == null ||
                _windowProcessor == null ||
                _networkService == null ||
                _mapStorageInterface == null ||
                _gameManager == null ||
                _mapDataProvider == null ||
                _uiDocument == null ||
                _playerStats == null ||
                _status == null ||
                _inventory == null ||
                _clan == null)
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

        private readonly List<Action> _unsubscribers = new();

        // Protocol packets may be value types, so this helper must remain unconstrained.
        private void Subscribe<T>(Action<T> handler)
        {
            _networkService.Subscribe(handler);
            _unsubscribers.Add(() => _networkService.Unsubscribe(handler));
        }

        private void TrySubscribeToNetworkService()
        {
            if (_networkService == null)
            {
                return;
            }

            // NetworkService deduplicates handlers. Re-registering here is
            // intentional: after a domain reload the injected service can be a
            // new instance while PacketHandler's _isSubscribed flag survives.
            // The old guard would then leave the new dispatcher empty.
            //
            // The undo list gets no such deduplication, and this method runs
            // again on every TryInitialize call. Rebuilding it in step with the
            // registrations below keeps it at one entry per subscription instead
            // of growing by the full set each time.
            _unsubscribers.Clear();

            Subscribe<WorldInitPacket>(_worldInit.Process);
            Subscribe<RobotInfoPacket>(_robotInfo.Process);
            Subscribe<PlayerInfoPacket>(_playerInfo.Process);
            Subscribe<MovementSpeedPacket>(_playerInfo.Process);
            Subscribe<OpenWindowPacket>(_windowProcessor.Process);
            Subscribe<CloseWindowPacket>(_windowProcessor.Process);
            Subscribe<RobotPositionPacket>(_robotPosition.Process);
            Subscribe<MapRegionPacket>(_mapRegion.Process);
            Subscribe<PackPacket>(_building.Process);
            Subscribe<RemovePackPacket>(_building.Process);

            Subscribe<LevelPacket>(_playerStats.Process);
            Subscribe<HealthPacket>(_playerStats.Process);
            Subscribe<CurrencyPacket>(_playerStats.Process);
            Subscribe<GeologyPacket>(_playerStats.Process);
            Subscribe<BasketPacket>(_playerStats.Process);
            Subscribe<MaxDepthPacket>(_playerStats.Process);

            Subscribe<AutoMineStatePacket>(PlayerState.Process);
            Subscribe<AggressionStatePacket>(PlayerState.Process);
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
            Subscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
            Subscribe<ShowClanPacket>(_clan.Process);
            Subscribe<HideClanPacket>(_clan.Process);
            Subscribe<MissionInitPacket>(_mission.Process);
            Subscribe<MissionProgressPacket>(_mission.Process);
            Subscribe<DisconnectPacket>(_connection.Process);
            Subscribe<ReconnectPacket>(_connection.Process);
            Subscribe<AuthTokenPacket>(HandleAuthTokenPacket);
            Subscribe<OpenURLPacket>(OpenURL.Process);
            Subscribe<MissionArrowPacket>(_missionArrow.Process);

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
        /// Any packet that lands in that window reaches a processor, which
        /// resolves a lazily-registered manager from an already-disposed
        /// container - and VContainer answers that by re-running the provider,
        /// i.e. by spawning a fresh BuildingManager / RobotManager /
        /// ServerAudioEventManager GameObject into the closing scene.
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

            if (_networkService != null)
            {
                foreach (Action unsubscribe in _unsubscribers)
                {
                    unsubscribe();
                }
            }

            _unsubscribers.Clear();
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
                // An empty token is a rejected authentication response, not a
                // client invariant failure. Keep the auth window/reconnect flow
                // alive without tripping the editor fail-fast logger.
                if (!_emptyAuthTokenWarningLogged)
                {
                    Debug.LogWarning("[Auth] Server returned an empty authentication token.");
                    _emptyAuthTokenWarningLogged = true;
                }

                return;
            }

            _emptyAuthTokenWarningLogged = false;
            Auth.AuthTokenManager.SaveToken(newToken);

            var gm = _gameManager;
            if (gm != null)
            {
                gm.AuthorizeUI();
            }
        }
    }
}
