using System;
using Fodinae.Scripts.Core;
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
        Disconnected
    }

    /// <summary>
    /// Единый менеджер жизненного цикла игры и сессии.
    ///
    /// Управляет высокими состояниями сессии и связывает событийно геймплейные подсистемы.
    /// </summary>
    public sealed class GameManager : SingletonMonoBehaviour<GameManager>
    {
        public GameState CurrentState { get; private set; } = GameState.Offline;
        public bool IsUIAuthorized { get; private set; }

        public event Action<GameState> OnGameStateChanged;
        public event Action OnWorldLoaded;

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

        public void AuthorizeUI()
        {
            IsUIAuthorized = true;
            Debug.Log("[GameManager] UI authorized");
        }

        public void DeauthorizeUI()
        {
            IsUIAuthorized = false;
            Debug.Log("[GameManager] UI deauthorized");
        }
    }
}
