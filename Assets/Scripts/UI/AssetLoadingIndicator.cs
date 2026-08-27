#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// LoaderContainer: защитный экран загрузки (fullscreen overlay) удерживается до
    /// события <see cref="GameManager.OnWorldLoaded"/>. После загрузки мира скрывается,
    /// оставляя маленькую «пимпочку» в правом нижнем углу — статус ассетов, FPS, пинг, версия.
    /// </summary>
    public sealed class AssetLoadingIndicator : MonoBehaviour
    {
        [Inject]
        private ClientAssetLoader _assetLoader = null!;

        [Inject]
        private FPSCounter _fpsCounter = null!;

        [Inject]
        private UIDocument _document = null!;

        [Inject]
        private TerrainRenderer _terrainRenderer = null!;

        [Inject]
        private ISessionContainer _session = null!;

        private GameManager? _gameManager;
        private VisualElement? _root;
        private VisualElement? _loadingOverlay;
        private Label? _loadingSpinnerLabel;
        private Label? _loadingStatusLabel;
        private Label? _loadingProgressLabel;
        private IVisualElementScheduledItem? _spinnerSchedule;
        private bool _loadingOverlayVisible;
        private float _nextRefreshTime;
        private bool _initialized;

        private void OnEnable()
        {
            if (_initialized)
            {
                RebindGameManager();
                return;
            }

            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_initialized || _session == null || _session.Current == null)
            {
                return;
            }

            _gameManager = _session.TryResolve<GameManager>();
            if (_gameManager == null || _assetLoader == null || _fpsCounter == null || _document == null || _terrainRenderer == null)
            {
                return;
            }

            _initialized = true;
            _gameManager.OnWorldLoaded += OnWorldLoaded;
            CreateUI();

            if (!_gameManager.IsWorldLoaded && _gameManager.IsUIAuthorized)
            {
                ShowLoadingOverlay();
            }

            Refresh();
        }

        private void RebindGameManager()
        {
            if (_session == null || _session.Current == null)
            {
                if (_gameManager != null)
                {
                    _gameManager.OnWorldLoaded -= OnWorldLoaded;
                    _gameManager = null;
                }

                return;
            }

            GameManager? current = _session.TryResolve<GameManager>();
            if (current == null)
            {
                return;
            }

            if (_gameManager != null && !ReferenceEquals(_gameManager, current))
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _gameManager = current;
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
            _gameManager.OnWorldLoaded += OnWorldLoaded;
        }

        private void OnDestroy()
        {
            _spinnerSchedule?.Pause();
            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _root?.RemoveFromHierarchy();
        }

        private void Update()
        {
            if (_initialized && _gameManager == null)
            {
                RebindGameManager();
            }

            if (!_initialized)
            {
                TryInitialize();
                return;
            }

            if (_root == null)
            {
                return;
            }

            if (_loadingOverlayVisible && _loadingStatusLabel != null)
            {
                _loadingStatusLabel.text = GetLoadingStatusText();
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        private string GetLoadingStatusText()
        {
            if (_gameManager == null || !_gameManager.IsUIAuthorized)
            {
                return "Инициализация подключения...";
            }

            bool terrainReady = _terrainRenderer?.IsReadyForGameplay ?? false;

            if (!terrainReady)
            {
                return "Загрузка ландшафта...";
            }

            if (_assetLoader == null)
            {
                return "Загрузка ресурсов...";
            }

            int pending = _assetLoader.PendingAssetCount;
            int queued = _assetLoader.QueuedAssetCount;

            return pending > 0 || queued > 0
                ? $"Загрузка ассетов: {pending} активных, {queued} в очереди"
                : "Готово к игре";
        }

        private void OnWorldLoaded()
        {
            HideLoadingOverlay();
        }

        private void ShowLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            _loadingOverlay.style.display = DisplayStyle.Flex;
            _loadingOverlay.pickingMode = PickingMode.Position;
            _loadingOverlayVisible = true;
        }

        private void HideLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            _loadingOverlay.style.display = DisplayStyle.None;
            _loadingOverlay.pickingMode = PickingMode.Ignore;
            _loadingOverlayVisible = false;
        }

        private void CreateUI()
        {
            if (_document?.rootVisualElement == null)
            {
                return;
            }

            var uiUxml = Resources.Load<VisualTreeAsset>("UI/AssetLoadingIndicator");
            if (uiUxml == null)
            {
                return;
            }

            VisualElement tree = uiUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _document.rootVisualElement.Add(tree);

            _root = tree;
            _loadingOverlay = tree.Q<VisualElement>("LoadingOverlay");
            _loadingSpinnerLabel = tree.Q<Label>("SpinnerLabel");
            _loadingStatusLabel = tree.Q<Label>("StatusLabel");
            _loadingProgressLabel = tree.Q<Label>("ProgressLabel");

            // This root is always present in the shared UIDocument. It must not
            // become a transparent fullscreen input shield while hidden.
            _root.pickingMode = PickingMode.Ignore;
            if (_loadingOverlay != null)
            {
                _loadingOverlay.pickingMode = PickingMode.Ignore;
            }

            StartSpinner();
            Refresh();
        }

        private void StartSpinner()
        {
            if (_loadingSpinnerLabel == null)
            {
                return;
            }

            string[] frames = ["\u25D0", "\u25D3", "\u25D1", "\u25D2"];
            _spinnerSchedule = _loadingSpinnerLabel.schedule.Execute(() =>
            {
                if (_loadingSpinnerLabel == null)
                {
                    return;
                }

                int frame = (int)(Time.unscaledTime * 4) % 4;
                _loadingSpinnerLabel.text = frames[frame];
            }).Every(250);
        }

        private void Refresh()
        {
            if (_assetLoader == null || _loadingStatusLabel == null || _loadingProgressLabel == null)
            {
                return;
            }

            _loadingStatusLabel.text = GetLoadingStatusText();
            UpdateProgressText();
        }

        private void UpdateProgressText()
        {
            if (_loadingProgressLabel == null || _assetLoader == null)
            {
                return;
            }

            int pending = _assetLoader.PendingAssetCount;
            int queued = _assetLoader.QueuedAssetCount;
            _loadingProgressLabel.text = pending > 0 || queued > 0
                ? $"Активно: {pending}, В очереди: {queued}"
                : string.Empty;
        }
    }
}
