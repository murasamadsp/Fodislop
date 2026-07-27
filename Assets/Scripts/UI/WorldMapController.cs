using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.UI
{
    public class WorldMapController : MonoBehaviour
    {
        private CameraFollow _cameraFollow;
        private PlayerMovementController _player;
        private TerrainRenderer _terrain;
        private WorldMapRenderer _mapRenderer;
        private InputAction _mapToggleAction;

        private bool _isInMapMode;
        private Vector3 _storedCamPos;
        private float _storedCamZoom;

        // HUD elements
        private Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView _playerHud;
        private Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView _inventory;
        private FPSCounter _fps;
        private MinimapController _minimap;
        private PauseMenu _pauseMenu;

        protected void Start()
        {
            _cameraFollow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerMovementController>();
            _terrain = UnityEngine.Object.FindAnyObjectByType<TerrainRenderer>();
            _playerHud = UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView>();
            _inventory = UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView>();
            _fps = UnityEngine.Object.FindAnyObjectByType<FPSCounter>();
            _minimap = UnityEngine.Object.FindAnyObjectByType<MinimapController>();
            _pauseMenu = UnityEngine.Object.FindAnyObjectByType<PauseMenu>();

            if (_cameraFollow == null)
            {
                Debug.LogError("[WorldMapController] CameraFollow not found");
                enabled = false;
                return;
            }

            _mapToggleAction = new InputAction("MapToggle", binding: "<Keyboard>/m");
            _mapToggleAction.performed += _ => ToggleMapMode();
            _mapToggleAction.Enable();
        }

        protected void OnDestroy()
        {
            _mapToggleAction?.Dispose();
        }

        private void ToggleMapMode()
        {
            if (!enabled)
            {
                return;
            }

            var mapStorage = Fodinae.Scripts.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
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

            var mapStorage = Fodinae.Scripts.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (mapStorage == null || !mapStorage.IsReady)
            {
                return;
            }

            _isInMapMode = true;
            _cameraFollow.SetScrollEnabled(false);

            // Store camera state
            _storedCamPos = _cameraFollow.transform.position;
            _storedCamZoom = _cameraFollow.GetCurrentZoom();

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

            // Center map on player position
            int CENTER_X = _player != null ? _player.Position.x : ((Fodinae.Scripts.Core.ServiceLocator.Resolve<MapManager>())?.WorldWidth ?? 64) / 2;
            int CENTER_Y = _player != null ? _player.Position.y : ((Fodinae.Scripts.Core.ServiceLocator.Resolve<MapManager>())?.WorldHeight ?? 64) / 2;
            _mapRenderer.SetViewCenter(CENTER_X, CENTER_Y);
        }

        private void ExitMapMode()
        {
            if (!_isInMapMode)
            {
                return;
            }

            _isInMapMode = false;
            _cameraFollow.SetScrollEnabled(true);

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
