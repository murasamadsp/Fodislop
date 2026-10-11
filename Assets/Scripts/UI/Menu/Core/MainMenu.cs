#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Kern.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour, ILocalizableUI
    {
        private const string GameSceneName = ProjectRuntimeContracts.SceneNames.MainGame;

        [SerializeField]
        private Texture2D? _shadeTexture;
        [SerializeField]
        private Texture2D? _spaceBgTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private readonly MenuLoaderPanel _loaderPanel = new();
        private readonly MenuModalManager _modalManager = new();
        private readonly MenuNavigationPresenter _navigationPresenter = new();

        private bool _loadingActive;
        private bool _built;
        private bool _subscribed;
        private bool _windowVisibilitySubscribed;
        private bool _teardownStarted;
        private CancellationTokenSource? _descentCancellation;

        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private IConnectionService _connection = null!;
        [Inject]
        private IWorldEntryPreparation _worldEntryPreparation = null!;
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private WindowCommandStream _windowCommands = null!;

        private MenuStarfield? _sceneStarfield;
        private MenuSceneryController? _sceneScenery;
        private MenuSceneryPresenter _sceneryPresenter = null!;

        [Inject]
        private void Construct(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _sceneryPresenter = new MenuSceneryPresenter(runtimeAssetPaths);
        }

        protected void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _built = false;
            }
        }

        protected void OnEnable()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_built && Application.isPlaying && _tree != null)
            {
                UIDocument doc = GetComponent<UIDocument>();
                if (doc == null || doc.rootVisualElement == null)
                {
                    // Реактивация — best-effort: панель может пересоздаться позже
                    // (повторный OnEnable документа); первичная сборка в Start
                    // уже прошла, поэтому тихий возврат не теряет экран.
                    return;
                }

                _root = doc.rootVisualElement;
                _root.pickingMode = PickingMode.Ignore;
                if (!_root.Contains(_tree))
                {
                    _root.Add(_tree);
                }
                SubscribeEvents();
                SubscribeWindowVisibility();
                _sceneryPresenter.Bind(_tree);
                _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

                if (_loc != null)
                {
                    _loc.RegisterLocalizable(this);
                    ApplyLocalizedText();
                }
            }
        }

        public void InitializeScene(MenuStarfield? starfield, MenuSceneryController? scenery)
        {
            _sceneStarfield = starfield;
            _sceneScenery = scenery;
            _sceneryPresenter.BindScene(starfield, scenery);

            if (_teardownStarted)
            {
                return;
            }

            if (_built && _tree != null)
            {
                return;
            }

            if (_built)
            {
                Debug.LogWarning("[MainMenu] _built was true but _tree is null (likely a hot-reload while in Play Mode) - rebuilding UI from scratch.");
                _built = false;
            }

            _doc = GetComponent<UIDocument>();
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_doc == null || _root == null)
            {
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument panel is not available at Start (панель создаётся в OnEnable документа и к Start обязана существовать).");
            }

            PanelSettings panelSettings = _doc.panelSettings ??
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument requires an authored PanelSettings asset.");

            var mainMenuUxml = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.MainMenuUxml);
            if (mainMenuUxml == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/Menus/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            VisualElement tree = mainMenuUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            _doc.panelSettings = panelSettings;
            UIScreen.Bind(_doc, _clientConfig?.Config.Interface.UIScale ?? 0f);
            _tree = tree;

            BindUIElements(tree);
            _modalManager.Bind(tree);
            _sceneryPresenter.Bind(tree);

            _subscribed = false;
            SubscribeEvents();
            SubscribeWindowVisibility();
            _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

            if (_loc != null)
            {
                _loc.RegisterLocalizable(this);
            }

            ApplyLocalizedText();
            _built = true;

            _sceneryPresenter.MarkUIBuilt();
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        public async UniTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            float timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                _sceneryPresenter.Tick(ref _spaceBgTexture);
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Сценка главного меню не готова за 3 секунды. " +
                $"Разметка построена={_built}; {_sceneryPresenter.DescribeReadiness()}.");
        }

        private void BindUIElements(VisualElement tree)
        {
            VisualElement searchRoot = _root ?? tree;
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? searchRoot.Q<VisualElement>("MainMenuContainer");
            _loaderPanel.Bind(tree, searchRoot, _loc);

            _navigationPresenter.Bind(
                tree,
                _modalManager,
                OnPlayButtonClicked,
                CancelDescent,
                _loc);

            _loaderPanel.Reset();
        }

        protected void Update()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (Application.isPlaying && !_built)
            {
                InitializeScene(_sceneStarfield, _sceneScenery);
                if (!_built)
                {
                    return;
                }
            }

            if (Application.isPlaying && _built && _doc != null && _tree != null)
            {
                var liveRoot = _doc.rootVisualElement;
                if (liveRoot == null || !ReferenceEquals(_tree.parent, liveRoot))
                {
                    _tree = null;
                    _built = false;
                    InitializeScene(_sceneStarfield, _sceneScenery);
                    return;
                }
            }

            // [ExecuteAlways]: вне Play Mode VContainer не вызывает Construct.
            if (!Application.isPlaying)
            {
                return;
            }

            if (_loadingActive)
            {
                UpdateLoaderProgress();
            }

            _sceneryPresenter.Tick(ref _spaceBgTexture);
            MenuKeyboardHandler.HandleInput(_modalManager, _loadingActive, OnPlayButtonClicked, CancelDescent);
        }

        private void UpdateLoaderProgress()
        {
            WorldLoadPhase phase = _loadProgress != null
                ? _loadProgress.CurrentPhase
                : WorldLoadPhase.Handshake;
            _loaderPanel.UpdateProgress(phase, ReleaseInputToGameplay);
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            if (_tree != null)
            {
                _modalManager.SubscribeEvents(
                    _tree,
                    OnPlayButtonClicked,
                    _clientConfig,
                    _sceneNavigator,
                    _operations,
                    _loc);
            }

            _subscribed = true;
        }

        public void OpenModal(VisualElement? modal) => _modalManager.OpenModal(modal);

        public void CloseCurrentModal() => _modalManager.CloseCurrentModal();

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(MainMenu));
            if (_tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_tree, _loc);
            _navigationPresenter.ApplyLocalization(_loc);
            _loaderPanel.RefreshLocalization();
            UILocalizer.AssertLocalized(_tree, _loc);
        }

        protected void OnDestroy()
        {
            _teardownStarted = true;
            if (_doc != null)
            {
                UIScreen.Unbind(_doc);
            }

            if (_windowVisibilitySubscribed && _windowCommands != null)
            {
                _windowCommands.OpenWindowVisibilityChanged -= OnServerWindowVisibilityChanged;
                _windowVisibilitySubscribed = false;
            }

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _descentCancellation?.Cancel();
            _descentCancellation?.Dispose();
            _descentCancellation = null;

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void HideMenu()
        {
            UIState.Hide(_mainMenuContainer);
        }

        private void ReleaseInputToGameplay()
        {
            _loadingActive = false;
            UIState.Hide(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }
        }

        private void SubscribeWindowVisibility()
        {
            if (_windowVisibilitySubscribed || _windowCommands == null)
            {
                return;
            }

            _windowCommands.OpenWindowVisibilityChanged += OnServerWindowVisibilityChanged;
            _windowVisibilitySubscribed = true;
            OnServerWindowVisibilityChanged(_windowCommands.HasOpenWindows);
        }

        private void OnServerWindowVisibilityChanged(bool visible)
        {
            if (_teardownStarted || !_loadingActive)
            {
                return;
            }

            UIState.SetHidden(_tree, visible);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }

            if (!visible)
            {
                _loaderPanel.Show();
                UpdateLoaderProgress();
            }
        }

        private void OnPlayButtonClicked()
        {
            if (_loadingActive || _teardownStarted)
            {
                return;
            }

            Debug.Log($"[Probe] T0 {UnityEngine.Time.realtimeSinceStartup:F3}");
            Debug.Log("[MainMenu] Play button clicked - initiating descent sequence");

            HideMenu();
            _modalManager.CloseCurrentModal();
            _loadingActive = true;
            _descentCancellation?.Dispose();
            _descentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                destroyCancellationToken);

            _loaderPanel.Show();
            _loaderPanel.Reset();

            if (_windowCommands != null && _windowCommands.HasOpenWindows)
            {
                OnServerWindowVisibilityChanged(visible: true);
            }

            _navigationPresenter.SetDescentRouteActive();

            _sceneryPresenter.DescentTarget = 1f;
            UpdateLoaderProgress();

            _operations.Run("main_menu_descent", RunDescentAsync);
        }

        private async UniTask RunDescentAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                _descentCancellation?.Token ?? CancellationToken.None);
            CancellationToken transitionToken = linkedCancellation.Token;
            try
            {
                // Подготовка мира не входит в таймаут перехода: он меряет только сцену.
                var preparationTimer = Stopwatch.StartNew();
                await _worldEntryPreparation.EnsureReadyAsync(transitionToken);
                Debug.Log(
                    $"[MainMenu] World-entry preparation completed in {preparationTimer.Elapsed.TotalSeconds:F2} s; " +
                    "starting MainGame scene transition.");
                transitionToken.ThrowIfCancellationRequested();
                await _sceneNavigator.TransitionAsync(GameSceneName, transitionToken);
            }
            catch (OperationCanceledException) when (transitionToken.IsCancellationRequested)
            {
            }
            catch (ConnectionStartupInterruptedException exception)
            {
                if (!_teardownStarted)
                {
                    // Не warning: вход в мир оборвался, а по консоли это было
                    // неотличимо от штатного спуска. Причина обязана быть
                    // слышна — иначе отказ ищут по одному экрану.
                    Debug.LogError(
                        $"[MainMenu] MainGame startup was interrupted by a disconnected transport; " +
                        $"descent remains active. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (TimeoutException exception) when (IsWaitingForWorldStartup())
            {
                if (!_teardownStarted)
                {
                    Debug.LogError(
                        $"[MainMenu] MainGame startup timed out; keeping the descent screen while " +
                        $"waiting for the server. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (Exception exception)
            {
                if (_teardownStarted)
                {
                    return;
                }

                _loaderPanel.ShowFailure(exception, _loc);
            }
        }

        private bool IsWaitingForWorldStartup() =>
            _connection.IsAutoReconnecting || _connection.IsWorldStartupPending;

        private void CancelDescent()
        {
            if (!_loaderPanel.HasFailure)
            {
                Debug.Log("[MainMenu] Descent is already in progress; waiting for MainGame.");
                return;
            }

            _connection.Disconnect();
            _descentCancellation?.Cancel();
            _loadingActive = false;
            _loaderPanel.Reset();
            _loaderPanel.Hide();
            _navigationPresenter.SetOrbitRouteActive();
            _sceneryPresenter.DescentTarget = 0f;
            UIState.Show(_mainMenuContainer);
        }
    }
}
