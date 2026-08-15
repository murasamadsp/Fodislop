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

namespace Fodinae.UI
{
    public class WorldMapController : MonoBehaviour
    {
        private CameraFollow? _cameraFollow;
        private PlayerMovementController? _player;
        private TerrainRenderer? _terrain;
        private WorldMapRenderer? _mapRenderer;
        private InputAction? _mapToggleAction;

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
        }

        private void TryInitialize()
        {
            if (_initialized || !ServiceLocator.IsInitialized)
            {
                return;
            }

            _cameraFollow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
            _player = PlayerMovementController.LocalPlayer;
            if (_player == null && !_playerSpawnSubscription)
            {
                PlayerMovementController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
                _playerSpawnSubscription = true;
            }
            _terrain = UnityEngine.Object.FindAnyObjectByType<TerrainRenderer>();
            _playerHud = UnityEngine.Object.FindAnyObjectByType<Fodinae.UI.HUD.Player.View.PlayerHUDView>();
            _inventory = UnityEngine.Object.FindAnyObjectByType<Fodinae.UI.HUD.Inventory.View.InventoryView>();
            _fps = UnityEngine.Object.FindAnyObjectByType<FPSCounter>();
            _minimap = UnityEngine.Object.FindAnyObjectByType<MinimapController>();

            if (_cameraFollow == null)
            {
                return;
            }

            _mapToggleAction = new InputAction("MapToggle", binding: "<Keyboard>/m");
            _mapToggleAction.performed += _ => ToggleMapMode();
            _mapToggleAction.Enable();
            _initialized = true;
        }

        protected void OnDestroy()
        {
            _mapToggleAction?.Dispose();
            PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        private void OnLocalPlayerSpawned(PlayerMovementController player)
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
            _playerSpawnSubscription = false;
            _player = player;
        }

        private void ToggleMapMode()
        {
            if (!enabled)
            {
                return;
            }

            if (!Fodinae.Core.ServiceLocator.IsInitialized)
            {
                throw new InvalidOperationException(
                    "[WorldMapController] Map toggle was requested before the VContainer resolver was initialized.");
            }

            var mapStorage = Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (mapStorage == null || !mapStorage.IsReady)
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

            var mapStorage = Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            _isInMapMode = true;
            _cameraFollow!.SetScrollEnabled(false);

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
