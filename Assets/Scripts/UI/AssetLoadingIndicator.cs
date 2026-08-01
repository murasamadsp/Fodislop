#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Small bottom-right asset loading indicator. The details panel is opened
    /// on demand so the HUD remains unobtrusive during normal gameplay.
    /// </summary>
    public sealed class AssetLoadingIndicator : MonoBehaviour
    {
        private ClientAssetLoader? _assetLoader;
        private FPSCounter? _fpsCounter;
        private UIDocument? _document;
        private VisualElement? _root;
        private Button? _toggleButton;
        private Label? _buttonLabel;
        private Label? _summaryLabel;
        private Label? _metricsLabel;
        private Label? _detailsLabel;
        private bool _detailsVisible;
        private float _nextRefreshTime;

        private void Awake()
        {
            _assetLoader = ServiceLocator.Resolve<ClientAssetLoader>();
        }

        private void Update()
        {
            if (_root == null)
            {
                TryCreateUI();
                return;
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        private void OnDestroy()
        {
            _root?.RemoveFromHierarchy();
        }

        private void TryCreateUI()
        {
            _assetLoader ??= ServiceLocator.Resolve<ClientAssetLoader>();
            _fpsCounter ??= FindAnyObjectByType<FPSCounter>();
            _document ??= FindAnyObjectByType<UIDocument>();
            if (_assetLoader == null || _document?.rootVisualElement == null)
            {
                return;
            }

            _root = new VisualElement();
            _root.AddToClassList("asset-status-root");

            _toggleButton = new Button(ToggleDetails)
            {
                tooltip = "Состояние загрузки ассетов",
            };
            _toggleButton.AddToClassList("asset-status-button");
            _buttonLabel = new Label("✓");
            _buttonLabel.AddToClassList("asset-status-button-label");
            _toggleButton.Add(_buttonLabel);
            _root.Add(_toggleButton);

            var panel = new VisualElement();
            panel.AddToClassList("asset-status-panel");

            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("asset-status-summary");
            panel.Add(_summaryLabel);

            _metricsLabel = new Label();
            _metricsLabel.AddToClassList("asset-status-metrics");
            panel.Add(_metricsLabel);

            _detailsLabel = new Label();
            _detailsLabel.AddToClassList("asset-status-details");
            panel.Add(_detailsLabel);
            panel.style.display = DisplayStyle.None;
            _root.Add(panel);

            _document.rootVisualElement.Add(_root);
            Refresh();
        }

        private void ToggleDetails()
        {
            _detailsVisible = !_detailsVisible;
            if (_root == null || _detailsLabel == null)
            {
                return;
            }

            var panel = _detailsLabel.parent;
            if (panel != null)
            {
                panel.style.display = _detailsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_assetLoader == null || _buttonLabel == null || _summaryLabel == null ||
                _metricsLabel == null || _detailsLabel == null)
            {
                return;
            }

            int pending = _assetLoader.PendingAssetCount;
            int queued = _assetLoader.QueuedAssetCount;
            bool isLoading = pending > 0 || queued > 0;

            _buttonLabel.text = isLoading ? $"↓ {pending}" : "✓";
            _toggleButton?.EnableInClassList("asset-status-loading", isLoading);
            _summaryLabel.text = isLoading
                ? $"Загрузка ассетов: {pending} активных, {queued} в очереди"
                : "Загрузка ассетов не выполняется";
            _metricsLabel.text = $"Версия: {Application.version}\n" +
                                 $"FPS: {_fpsCounter?.CurrentFps ?? 0f:F1}\n" +
                                 $"Ping: {_fpsCounter?.PingMs ?? 0} ms\n" +
                                 $"Online: {_fpsCounter?.OnlinePlayers ?? 0}";

            if (!_detailsVisible)
            {
                return;
            }

            if (pending == 0)
            {
                _detailsLabel.text = queued > 0
                    ? "Ожидание отправки запроса..."
                    : "Нет активных загрузок.";
                return;
            }

            string[] names = _assetLoader.GetPendingAssetNames();
            int visibleCount = Math.Min(names.Length, 8);
            string details = string.Empty;
            for (int i = 0; i < visibleCount; i++)
            {
                details += $"• {names[i]}\n";
            }

            if (names.Length > visibleCount)
            {
                details += $"и ещё {names.Length - visibleCount}...";
            }

            _detailsLabel.text = details;
        }
    }
}
