#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.World.Terrain;
using UnityEngine;

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

            _uiSetup = true;
            SetupUI();
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
                AddInjectedComponents(
                    invGO,
                    typeof(Fodinae.UI.HUD.Inventory.View.InventoryView),
                    typeof(Fodinae.UI.HUD.Inventory.Presenter.InventoryPresenter));
            }

            if (UnityEngine.Object.FindAnyObjectByType<Fodinae.UI.HUD.Player.View.PlayerHUDView>(FindObjectsInactive.Include) == null)
            {
                var hudGO = new GameObject("PlayerHUD");
                hudGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponents(
                    hudGO,
                    typeof(Fodinae.UI.HUD.Player.View.PlayerHUDView),
                    typeof(Fodinae.UI.HUD.Player.Presenter.PlayerHUDPresenter));
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

            if (UnityEngine.Object.FindAnyObjectByType<GameErrorUI>(FindObjectsInactive.Include) == null)
            {
                var errorGO = new GameObject("ErrorUI");
                errorGO.transform.SetParent(_uiRoot.transform);
                AddInjectedComponent<GameErrorUI>(errorGO);
            }

            var arrowGO = new GameObject("MissionArrowUI");
            arrowGO.transform.SetParent(_uiRoot.transform);
            AddInjectedComponent<MissionArrowUI>(arrowGO);
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

            TerrainRenderer? terrain = TerrainRenderer.Instance;
            if (terrain == null || !terrain.IsReadyForGameplay)
            {
                return;
            }

            _worldLoadPending = false;
            _worldLoadPublished = true;
            IsWorldLoaded = true;
            SetState(GameState.InGame);
            player.SetGameplayVisible();
            CameraFollow.Instance?.SnapToTarget();
            Debug.Log("[GameManager] World load completed: server position and terrain are ready.");
            OnWorldLoaded?.Invoke();
        }

        // Runtime-created components never reach GameLifetimeScope's startup injection
        // scan — inject explicitly so their [Inject] fields are filled immediately.
        // The temporary SetActive(false) ensures OnEnable/Start are not invoked before
        // injection completes, matching VContainer's NewGameObjectProvider ordering.
        private static void AddInjectedComponent<T>(GameObject go)
            where T : Component
        {
            AddInjectedComponents(go, typeof(T));
        }

        private static void AddInjectedComponents(GameObject go, params Type[] componentTypes)
        {
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            try
            {
                for (int i = 0; i < componentTypes.Length; i++)
                {
                    Component component = go.AddComponent(componentTypes[i]);
                    Fodinae.Core.ServiceLocator.Inject(component);
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
