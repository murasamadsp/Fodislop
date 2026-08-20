#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.World;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.UI
{
    public class WorldMapController : MonoBehaviour
    {
        [Inject]
        private IObjectResolver _resolver = null!;
        [Inject]
        private CameraFollow? _injectedCameraFollow;
        [Inject]
        private TerrainRenderer? _injectedTerrain;
        [Inject]
        private Fodinae.UI.HUD.Player.View.PlayerHUDView? _injectedPlayerHud;
        [Inject]
        private Fodinae.UI.HUD.Inventory.View.InventoryView? _injectedInventory;
        [Inject]
        private FPSCounter? _injectedFps;
        [Inject]
        private MinimapController? _injectedMinimap;

        private CameraFollow? _cameraFollow;
        private PlayerMovementController? _player;
        private TerrainRenderer? _terrain;
        private WorldMapRenderer? _mapRenderer;

        private bool _isInMapMode;
        private bool _initialized;
        private bool _playerSpawnSubscription;

        // HUD elements
        private Fodinae.UI.HUD.Player.View.PlayerHUDView? _playerHud;
        private Fodinae.UI.HUD.Inventory.View.InventoryView? _inventory;
        private FPSCounter? _fps;
        private MinimapController? _minimap;

        protected void Start()
        {
            TryInitialize();
        }

        protected void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
            }

            // Map toggle as a direct keyboard check (mirrors MinimapController's N key);
            // ad-hoc InputActions are banned by the project lint policy.
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                ToggleMapMode();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_resolver == null)
            {
                // [Inject]-поля приходят в PostStart; Update ретраит TryInitialize.
                return;
            }

            _cameraFollow = _injectedCameraFollow ?? _resolver.Resolve<CameraFollow>();
            _terrain = _injectedTerrain ?? _resolver.Resolve<TerrainRenderer>();
            _playerHud = _injectedPlayerHud ?? _resolver.Resolve<Fodinae.UI.HUD.Player.View.PlayerHUDView>();
            _inventory = _injectedInventory ?? _resolver.Resolve<Fodinae.UI.HUD.Inventory.View.InventoryView>();
            _fps = _injectedFps ?? _resolver.Resolve<FPSCounter>();
            _minimap = _injectedMinimap ?? _resolver.Resolve<MinimapController>();

            _player = PlayerMovementController.LocalPlayer;
            if (_player == null && !_playerSpawnSubscription)
            {
                PlayerMovementController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
                _playerSpawnSubscription = true;
            }

            _initialized = true;
        }

        protected void OnDestroy()
        {
            UnsubscribeFromPlayerSpawn();
        }

        protected void OnDisable()
        {
            UnsubscribeFromPlayerSpawn();
            _initialized = false;
        }

        private void OnLocalPlayerSpawned(PlayerMovementController player)
        {
            UnsubscribeFromPlayerSpawn();
            _player = player;
        }

        private void UnsubscribeFromPlayerSpawn()
        {
            if (!_playerSpawnSubscription)
            {
                return;
            }

            PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
            _playerSpawnSubscription = false;
        }

        private void ToggleMapMode()
        {
            if (!enabled)
            {
                return;
            }

            if (_resolver == null)
            {
                throw new InvalidOperationException(
                    "[WorldMapController] Map toggle was requested before the VContainer resolver was initialized.");
            }

            MapStorage mapStorage = _resolver.Resolve<MapStorage>() ??
                throw new InvalidOperationException(
                    "[WorldMapController] MapStorage is not registered.");
            if (!mapStorage.IsReady)
            {
                return;
            }

            if (_isInMapMode)
            {
                ExitMapMode();
            }
            else
            {
                EnterMapMode();
            }
        }

        private void EnterMapMode()
        {
            if (_isInMapMode)
            {
                return;
            }

            PlayerMovementController? player = _player ?? PlayerMovementController.LocalPlayer;
            if (player == null || !player.HasServerPosition)
            {
                throw new InvalidOperationException(
                    "[WorldMapController] Cannot enter map mode before the local player has a server position.");
            }

            _player = player;

            MapStorage mapStorage = _resolver.Resolve<MapStorage>() ??
                throw new InvalidOperationException(
                    "[WorldMapController] MapStorage is not registered.");
            if (!mapStorage.IsReady)
            {
                return;
            }

            _isInMapMode = true;
            if (_cameraFollow != null)
            {
                _cameraFollow.SetScrollEnabled(false);
            }

            if (_terrain != null)
            {
                _terrain.enabled = false;
            }

            if (_mapRenderer == null)
            {
                var go = new GameObject("WorldMapRenderer");
                _mapRenderer = go.AddComponent<WorldMapRenderer>();
            }

            _mapRenderer.Show();

            SetHudVisible(false);

            _mapRenderer.SetViewCenter(player.Position.x, player.Position.y);
        }

        private void ExitMapMode()
        {
            if (!_isInMapMode)
            {
                return;
            }

            _isInMapMode = false;
            if (_cameraFollow != null)
            {
                _cameraFollow.SetScrollEnabled(true);
            }

            if (_mapRenderer != null)
            {
                _mapRenderer.Hide();
            }

            if (_terrain != null)
            {
                _terrain.enabled = true;
            }

            SetHudVisible(true);
        }

        private void SetHudVisible(bool visible)
        {
            if (_playerHud != null)
            {
                _playerHud.enabled = visible;
            }

            if (_inventory != null)
            {
                _inventory.enabled = visible;
            }

            if (_fps != null)
            {
                _fps.enabled = visible;
            }

            if (_minimap != null)
            {
                _minimap.enabled = visible;
            }
        }
    }
}
