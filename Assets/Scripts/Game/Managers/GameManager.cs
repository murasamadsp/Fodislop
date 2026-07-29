#nullable enable

using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Player.Model;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
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

        public event Action<GameState>? OnGameStateChanged;
        public event Action? OnWorldLoaded;

        private GameObject _uiRoot;

        private void Awake()
        {
            SetupUI();
        }

        private void OnDestroy()
        {
            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
            }
        }

        private void SetupUI()
        {
            _uiRoot = new GameObject("UIRoot");
            _uiRoot.SetActive(false);
            _uiRoot.transform.SetParent(transform);

            if (UnityEngine.Object.FindAnyObjectByType<MinimapController>() == null)
            {
                var mmGO = new GameObject("MinimapRoot");
                AddInjectedComponent<MinimapController>(mmGO);
                mmGO.transform.SetParent(_uiRoot.transform);
            }

            var reconnectGO = new GameObject("ReconnectUI");
            AddInjectedComponent<ReconnectUI>(reconnectGO);
            reconnectGO.transform.SetParent(transform);

            if (UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView>() == null)
            {
                var invGO = new GameObject("InventoryRoot");
                AddInjectedComponent<Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView>(invGO);
                AddInjectedComponent<Fodinae.Scripts.UI.HUD.Inventory.Presenter.InventoryPresenter>(invGO);
                invGO.transform.SetParent(_uiRoot.transform);
            }

            if (UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView>() == null)
            {
                var hudGO = new GameObject("PlayerHUD");
                AddInjectedComponent<Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView>(hudGO);
                AddInjectedComponent<Fodinae.Scripts.UI.HUD.Player.Presenter.PlayerHUDPresenter>(hudGO);
                hudGO.transform.SetParent(_uiRoot.transform);
            }

            if (UnityEngine.Object.FindAnyObjectByType<PauseMenu>() == null)
            {
                var pauseGO = new GameObject("PauseMenu");
                AddInjectedComponent<PauseMenu>(pauseGO);
                pauseGO.transform.SetParent(_uiRoot.transform);
            }

            if (UnityEngine.Object.FindAnyObjectByType<GlobalChatUI>() == null)
            {
                var chatGO = new GameObject("ChatSystem");
                AddInjectedComponent<LocalChatPopup>(chatGO);
                AddInjectedComponent<GlobalChatUI>(chatGO);
                AddInjectedComponent<FloatingChatManager>(chatGO);
                chatGO.transform.SetParent(_uiRoot.transform);
            }

            var arrowGO = new GameObject("MissionArrowUI");
            AddInjectedComponent<MissionArrowUI>(arrowGO);
            arrowGO.transform.SetParent(_uiRoot.transform);
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
            Debug.Log("[GameManager] World load completed, notifying listeners.");
            OnWorldLoaded?.Invoke();
        }

        // Runtime-created components never reach GameLifetimeScope's startup injection
        // scan — inject explicitly so their [Inject] fields are filled immediately.
        private static void AddInjectedComponent<T>(GameObject go)
            where T : Component
        {
            var comp = go.AddComponent<T>();
            Fodinae.Scripts.Core.ServiceLocator.Inject(comp);
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
