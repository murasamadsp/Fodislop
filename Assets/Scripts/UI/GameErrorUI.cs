#nullable enable

using System;
using System.Threading;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Networking.Connection;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Централизованный обработчик ошибок. Показывает UI overlay для критических и
    /// не критических ошибок. Fail-fast: при фатальной ошибке инициирует
    /// TriggerDisconnect или немедленный выход, не пытаясь восстановиться.
    /// </summary>
    public sealed class GameErrorUI : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;

        private VisualElement? _errorPanel;
        private Label? _errorMessageLabel;
        private Button? _closeButton;
        private bool _isCritical;
        private bool _isInitialized;
        private static int? _mainThreadId;

        private bool IsMainThread
        {
            get
            {
                if (!_mainThreadId.HasValue)
                {
                    _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                }

                return Thread.CurrentThread.ManagedThreadId == _mainThreadId.Value;
            }
        }

        public static void ReportError(string message, Exception? ex = null)
        {
            Debug.LogWarning($"[GameError] {message}{(ex != null ? $"\n{ex}" : string.Empty)}");

            var self = ResolveInstance();
            if (self != null)
            {
                self.ShowError(message, false);
                return;
            }

            Debug.LogWarning($"[GameError] GameErrorUI не найден — ошибка не отображена в UI. Сообщение: {message}");
        }

        public static void ReportFatal(string message, Exception? ex = null)
        {
            string fullMessage = ex != null ? $"{message}\n\n{ex.Message}" : message;
            Debug.LogError($"[GameError] FATAL: {fullMessage}");

            var connectionService = ServiceLocator.Resolve<IConnectionService>();
            if (connectionService != null && connectionService.IsOffline)
            {
                connectionService.TriggerDisconnect(fullMessage);
                return;
            }

            var self = ResolveInstance();
            if (self != null)
            {
                self.ShowError(fullMessage, true);
                return;
            }

            Debug.Log("[GameError] Нет UI для отображения фатальной ошибки — Application.Quit().");
#if UNITY_EDITOR
            Debug.Break();
#else
            Application.Quit();
#endif
        }

        private static GameErrorUI? ResolveInstance()
        {
            try
            {
                var resolved = ServiceLocator.Resolve<GameErrorUI>();
                if (resolved != null)
                {
                    return resolved;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameError] ServiceLocator.Resolve<GameErrorUI> failed: {ex.Message}");
            }

            return FindAnyObjectByType<GameErrorUI>();
        }

        private void Start()
        {
            CreateUI();
        }

        private void OnDestroy()
        {
            _errorPanel?.RemoveFromHierarchy();
            _errorPanel = null;
        }

        private void CreateUI()
        {
            if (_doc?.rootVisualElement == null)
            {
                return;
            }

            _errorPanel = new VisualElement();
            ApplyErrorPanelStyle(_errorPanel);
            _doc.rootVisualElement.Add(_errorPanel);

            var titleLabel = new Label("<b>Ошибка</b>");
            titleLabel.AddToClassList("error-title");
            _errorPanel.Add(titleLabel);

            _errorMessageLabel = new Label();
            _errorMessageLabel.AddToClassList("error-message");
            _errorPanel.Add(_errorMessageLabel);

            _closeButton = new Button(OnCloseClicked);
            _closeButton.AddToClassList("error-close-button");
            _errorPanel.Add(_closeButton);

            _isInitialized = true;
            Hide();
        }

        private static void ApplyErrorPanelStyle(VisualElement panel)
        {
            panel.AddToClassList("ui-overlay");
            panel.AddToClassList("ui-overlay--blocking");
            panel.AddToClassList("game-error-overlay");
            panel.style.display = DisplayStyle.None;
            panel.SetEnabled(false);
            panel.pickingMode = PickingMode.Ignore;
        }

        private void ShowError(string message, bool isCritical)
        {
            if (!IsMainThread)
            {
                Debug.LogError("[GameError] ShowError called from background thread — skipping UI update.");
                return;
            }

            if (!_isInitialized)
            {
                CreateUI();
                if (!_isInitialized)
                {
                    Debug.LogError("[GameError] Cannot show error UI — initialization failed.");
                    return;
                }
            }

            _isCritical = isCritical;
            _errorMessageLabel!.text = message;
            _errorPanel!.style.display = DisplayStyle.Flex;
            UIContainerLayers.SetInteractive(_doc, UIContainerLayers.Blocking, true);
            _errorPanel.SetEnabled(true);
            _errorPanel.pickingMode = PickingMode.Position;

            _closeButton!.text = isCritical ? "Выход" : "ОК";

            _doc?.rootVisualElement?.Blur();
        }

        private void Hide()
        {
            _errorPanel!.style.display = DisplayStyle.None;
            UIContainerLayers.SetInteractive(_doc, UIContainerLayers.Blocking, false);
            _errorPanel.SetEnabled(false);
            _errorPanel.pickingMode = PickingMode.Ignore;
        }

        private void OnCloseClicked()
        {
            Hide();

            if (_isCritical)
            {
#if UNITY_EDITOR
                Debug.Break();
#else
                Application.Quit();
#endif
            }
        }
    }
}
