#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    /// <summary>
    /// Высокоуровневые состояния игрового сеанса.
    /// Расширяют сетевой статус <see cref="MinesServer.Networking.Shared.ConnectionStatus"/>,
    /// разделяя состояния оффлайн режима, подключения, геймплея и дисконнекта.
    /// </summary>
    public enum GameState
    {
        Offline,
        Connecting,
        InGame,
        Disconnected,
    }

    /// <summary>
    /// Единый менеджер жизненного цикла игры и сессии.
    ///
    /// Управляет высокими состояниями сессии и связывает событийно геймплейные подсистемы.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public GameState CurrentState { get; private set; } = GameState.Offline;
        public bool IsUIAuthorized { get; private set; }
        public bool IsWorldLoaded { get; private set; }

        public event Action<GameState>? OnGameStateChanged;
        public event Action? OnWorldLoaded;

        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private ITextureService _textureService = null!;
        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private IPlayerStats _playerStats = null!;
        [Inject]
        private IObjectResolver _resolver = null!;
        [Inject]
        private TerrainRenderer _terrainRenderer = null!;

        private GameObject? _uiRoot;
        private bool _worldLoadPending;
        private bool _worldLoadPublished;
        private bool _uiSetup;

        private void OnDestroy()
        {
            SharedMaterialCache.Clear();
            ItemRegistry.Clear();

            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
            }
        }

        public void EnsureUISetup()
        {
            if (_uiSetup)
            {
                return;
            }

            try
            {
                SetupUI();
                _uiSetup = true;
            }
            catch
            {
                if (_uiRoot != null)
                {
                    Destroy(_uiRoot);
                    _uiRoot = null;
                }

                _uiSetup = false;
                throw;
            }
        }

        private void SetupUI()
        {
            _uiRoot = new GameObject("UIRoot");
            _uiRoot.SetActive(false);
            _uiRoot.transform.SetParent(transform);

            if (UnityEngine.Object.FindAnyObjectByType<ReconnectUI>(FindObjectsInactive.Include) == null)
            {
                var reconnectGO = new GameObject("ReconnectUI");
                reconnectGO.transform.SetParent(transform);
                AddInjectedComponent<ReconnectUI>(reconnectGO);
            }

            if (UnityEngine.Object.FindAnyObjectByType<Fodinae.UI.HUD.Inventory.View.InventoryView>(FindObjectsInactive.Include) == null)
            {
                var invGO = new GameObject("InventoryRoot");
                invGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<Fodinae.UI.HUD.Inventory.View.InventoryView>(invGO);
            }

            if (UnityEngine.Object.FindAnyObjectByType<Fodinae.UI.HUD.Player.View.PlayerHUDView>(FindObjectsInactive.Include) == null)
            {
                var hudGO = new GameObject("PlayerHUD");
                hudGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<Fodinae.UI.HUD.Player.View.PlayerHUDView>(hudGO);
            }

            if (UnityEngine.Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include) == null)
            {
                var pauseGO = new GameObject("PauseMenu");
                pauseGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<PauseMenu>(pauseGO);
            }

            if (UnityEngine.Object.FindAnyObjectByType<GlobalChatUI>(FindObjectsInactive.Include) == null)
            {
                var chatGO = new GameObject("ChatSystem");
                chatGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponents(
                    chatGO,
                    typeof(LocalChatPopup),
                    typeof(GlobalChatUI),
                    typeof(FloatingChatManager));
            }

            if (UnityEngine.Object.FindAnyObjectByType<AssetLoadingIndicator>(FindObjectsInactive.Include) == null)
            {
                var loaderGO = new GameObject("LoaderContainer");
                loaderGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<AssetLoadingIndicator>(loaderGO);
            }

            if (UnityEngine.Object.FindAnyObjectByType<MissionArrowUI>(FindObjectsInactive.Include) == null)
            {
                var arrowGO = new GameObject("MissionArrowUI");
                arrowGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<MissionArrowUI>(arrowGO);
            }
        }

        public void SetState(GameState newState)
        {
            if (CurrentState == newState)
            {
                return;
            }

            CurrentState = newState;
            Debug.Log($"[GameManager] Game state changed to: {newState}");
            OnGameStateChanged?.Invoke(newState);
        }

        public void NotifyWorldLoaded()
        {
            // WorldInit can arrive again after reconnect or an offline-world
            // restart. A published load belongs to the previous world session
            // and must never suppress the next load notification.
            IsWorldLoaded = false;
            _worldLoadPublished = false;
            _worldLoadPending = true;
            TryPublishWorldLoaded();
        }

        private void Update()
        {
            if (_worldLoadPending)
            {
                TryPublishWorldLoaded();
            }
        }

        private void TryPublishWorldLoaded()
        {
            if (_worldLoadPublished)
            {
                return;
            }

            PlayerMovementController? player = PlayerMovementController.LocalPlayer;
            if (player == null || !player.HasServerPosition)
            {
                return;
            }

            Robot? robot = player.GetComponent<Robot>();
            if (robot == null || !robot.IsMetadataLoaded)
            {
                return;
            }

            if (_playerStats == null || !_playerStats.IsReady)
            {
                return;
            }

            TerrainRenderer? terrain = _terrainRenderer;
            if (terrain == null || !terrain.IsReadyForGameplay)
            {
                return;
            }

            // Terrain geometry being ready doesn't mean its textures (or robot sprites,
            // loaded through the same pipeline) have actually arrived yet — without this,
            // the loading screen hides while assets are still visibly popping in.
            if (_assetLoader is ClientAssetLoader clientAssetLoader &&
                (clientAssetLoader.PendingAssetCount > 0 || clientAssetLoader.QueuedAssetCount > 0))
            {
                return;
            }

            // ClientAssetLoader only tracks requests that have reached it — a cell texture
            // RequestTexture() just fired this frame hasn't reached ClientAssetLoader yet
            // (WorldTextureManager's own async chain yields once before enqueueing there).
            // PendingCellTextureRequests is set synchronously at the RequestTexture call
            // site, so it catches that gap.
            if (_textureService.PendingCellTextureRequests > 0)
            {
                return;
            }

            _worldLoadPending = false;
            _worldLoadPublished = true;
            IsWorldLoaded = true;
            Debug.Log($"[Probe] WorldLoaded {UnityEngine.Time.realtimeSinceStartup:F3}");
            SetState(GameState.InGame);
            player.SetGameplayVisible();
            _resolver.Resolve<CameraFollow>().SnapToTarget();
            AuthorizeUI();
            int robotCount = _robotService?.RobotCount ?? -1;
            Debug.Log(
                $"[GameManager] World load completed: server position and terrain are ready. " +
                $"robots={robotCount}, pendingAssets={(_assetLoader is ClientAssetLoader c ? c.PendingAssetCount : -1)}, " +
                $"queuedAssets={(_assetLoader is ClientAssetLoader c2 ? c2.QueuedAssetCount : -1)}, " +
                $"pendingCellTextures={_textureService.PendingCellTextureRequests}");
            OnWorldLoaded?.Invoke();
        }

        // Runtime-created components never reach GameLifetimeScope's startup injection
        // scan — inject explicitly so their [Inject] fields are filled immediately.
        // The temporary SetActive(false) ensures OnEnable/Start are not invoked before
        // injection completes, matching VContainer's NewGameObjectProvider ordering.
        private void AddInjectedComponent<T>(GameObject go)
            where T : Component
        {
            AddInjectedComponents(go, typeof(T));
        }

        private void AddInjectedComponents(GameObject go, params Type[] componentTypes)
        {
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            try
            {
                for (int i = 0; i < componentTypes.Length; i++)
                {
                    Component component = go.AddComponent(componentTypes[i]);
                    _resolver.Inject(component);
                }
            }
            finally
            {
                go.SetActive(wasActive);
            }
        }

        public void AuthorizeUI()
        {
            IsUIAuthorized = true;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(true);
            }

            Debug.Log("[GameManager] UI authorized");
        }

        public void DeauthorizeUI()
        {
            IsUIAuthorized = false;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(false);
            }

            Debug.Log("[GameManager] UI deauthorized");
        }
    }
}
