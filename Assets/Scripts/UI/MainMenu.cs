#nullable enable

using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        private const string GameSceneName = "MainGame";
        private const float GameScopeReadyTimeoutSeconds = 10f;

        // USS modifiers toggled on the station badge so it flips to whichever
        // side of the marker has room instead of sliding off-screen.

        [SerializeField]
        private Texture2D? _shadeTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private VisualElement? _loaderContainer;
        private VisualElement? _loaderContent;
        private MenuLoaderProgress? _loaderProgress;
        // Шаги маршрута в футере. Это контейнеры, а не Label: внутри каждого
        // лежит ромб активного шага и подпись.
        private VisualElement? _routeOrbit;
        private VisualElement? _routeDescent;
        private VisualElement? _routeSurface;

        // Кнопки основного экрана
        private Button? _playButton;
        private Button? _serverSelectButton;
        private Button? _updateAlertBanner;
        private Button? _userPillButton;
        private Button? _cancelDescentButton;

        // Правая боковая панель (Genshin Sidebar)
        private Button? _sideChronicleButton;
        private Button? _sideSettingsButton;
        private Button? _sideRepairButton;
        private Button? _sideUpdateButton;
        private Button? _sideDiscordButton;
        private Button? _sideTelegramButton;
        private Button? _sideVkButton;
        private Button? _sideExitButton;

        // Футер
        private Button? _newsTickerButton;
        private Button? _footerVersionButton;

        // Модальный слой
        private VisualElement? _modalOverlay;
        private VisualElement? _serverBrowserModal;
        private VisualElement? _settingsModal;
        private VisualElement? _chronicleModal;
        private VisualElement? _repairModal;
        private VisualElement? _profileModal;
        private VisualElement? _updateModal;
        private VisualElement? _activeModal;

        // Модалка настроек: табы
        private Button? _settingsTabGraphics;
        private Button? _settingsTabAudio;
        private Button? _settingsTabControls;
        private Button? _settingsTabNetwork;
        private VisualElement? _settingsPaneGraphics;
        private VisualElement? _settingsPaneAudio;
        private VisualElement? _settingsPaneControls;
        private VisualElement? _settingsPaneNetwork;

        // Модалка выбора серверов
        private Button? _serverItemHades;
        private Button? _serverItemTartarus;
        private Button? _serverItemCyber;
        private Button? _confirmServerButton;

        private bool _loadingActive;
        private bool _dismissedForServerWindow;
        private GameManager? _gameManager;
        private bool _built;
        private bool _subscribed;
        private bool _teardownStarted;
        private CancellationTokenSource? _descentCancellation;

        [Inject]
        private ISessionContainer? _session;
        [Inject]
        private ISceneCoordinator _sceneCoordinator = null!;

        private ISessionContainer? Session => _session?.Current != null ? _session : null;

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

            _doc = GetComponent<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            _root = _doc.rootVisualElement;

            // _built alone is not proof the cached VisualElement bindings are
            // still valid: a script hot-reload while already in Play Mode
            // preserves this MonoBehaviour's fields (including _built) via
            // Unity's backup/restore, but _tree and friends are plain C#
            // objects, not UnityEngine.Object - they come back null. Trusting
            // _built alone here left every binding null and every texture
            // apply silently failing after any mid-session recompile.
            if (_built && Application.isPlaying && _tree != null)
            {
                RebindGameManager();
                SubscribeEvents();

                // The presenter is a plain field, so a domain reload hands us a
                // fresh one with no elements resolved while _tree survived.
                // Rebinding is idempotent, so it is safe on the normal path too.
                _sceneryPresenter.Bind(_tree);
                _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);
                return;
            }

            if (_built && _tree == null)
            {
                Debug.LogWarning("[MainMenu] _built was true but _tree is null (likely a hot-reload while in Play Mode) - rebuilding UI from scratch.");
                _built = false;
            }

            var mainMenuUXML = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.MainMenuUxml);
            if (mainMenuUXML == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            VisualElement tree = mainMenuUXML.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);
            _tree = tree;

            // Тир раскладки вместо @media: класс на корне, границы и значения
            // совпадают с visual/main-menu-mirror/css/tokens.css §3.
            UILayoutTier.Attach(tree);

            BindUIElements(tree);
            _sceneryPresenter.Bind(tree);

            // Новое дерево — подписки предыдущего экземпляра недействительны
            // (OnDisable больше не сбрасывает флаг, чтобы не дублировать клики
            // на живом дереве). Сбрасываем только здесь, при полной перестройке.
            _subscribed = false;
            SubscribeEvents();
            _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

            _built = true;

            _sceneryPresenter.MarkUIBuilt();
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        [SerializeField]
        private Texture2D? _spaceBgTexture;

        private readonly MenuSceneryPresenter _sceneryPresenter = new();

        private void BindUIElements(VisualElement tree)
        {

            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer");
            _loaderContainer = tree.Q<VisualElement>("LoaderContainer");
            _loaderProgress = new MenuLoaderProgress(
                tree.Q<VisualElement>("LoaderProgressFill"),
                tree.Q<Label>("LoaderPhaseLabel"),
                tree.Q<Label>("LoaderPhaseCount"),
                tree.Q<VisualElement>("LoaderPhaseList"));
            _loaderContent = tree.Q<VisualElement>("LoaderContent");
            _routeOrbit = tree.Q<VisualElement>("MainMenuRouteOrbit");
            _routeDescent = tree.Q<VisualElement>("MainMenuRouteDescent");
            _routeSurface = tree.Q<VisualElement>("MainMenuRouteSurface");

            // Кнопки основного меню
            _playButton = tree.Q<Button>("PlayButton");
            _serverSelectButton = tree.Q<Button>("ServerSelectButton");
            _updateAlertBanner = tree.Q<Button>("UpdateAlertBanner");
            _userPillButton = tree.Q<Button>("UserPillButton");
            _cancelDescentButton = tree.Q<Button>("CancelDescentButton");

            // Правая боковая панель
            _sideChronicleButton = tree.Q<Button>("SideChronicleButton");
            _sideSettingsButton = tree.Q<Button>("SideSettingsButton");
            _sideRepairButton = tree.Q<Button>("SideRepairButton");
            _sideUpdateButton = tree.Q<Button>("SideUpdateButton");
            _sideDiscordButton = tree.Q<Button>("SideDiscordButton");
            _sideTelegramButton = tree.Q<Button>("SideTelegramButton");
            _sideVkButton = tree.Q<Button>("SideVkButton");
            _sideExitButton = tree.Q<Button>("SideExitButton");

            // Футер
            _newsTickerButton = tree.Q<Button>("NewsTickerButton");
            _footerVersionButton = tree.Q<Button>("FooterVersionButton");

            // Модалки
            _modalOverlay = tree.Q<VisualElement>("ModalOverlay");
            _serverBrowserModal = tree.Q<VisualElement>("ServerBrowserModal");
            _settingsModal = tree.Q<VisualElement>("SettingsModal");
            _chronicleModal = tree.Q<VisualElement>("ChronicleModal");
            _repairModal = tree.Q<VisualElement>("RepairModal");
            _profileModal = tree.Q<VisualElement>("ProfileModal");
            _updateModal = tree.Q<VisualElement>("UpdateModal");

            // Настройки табы
            _settingsTabGraphics = tree.Q<Button>("SettingsTabGraphics");
            _settingsTabAudio = tree.Q<Button>("SettingsTabAudio");
            _settingsTabControls = tree.Q<Button>("SettingsTabControls");
            _settingsTabNetwork = tree.Q<Button>("SettingsTabNetwork");
            _settingsPaneGraphics = tree.Q<VisualElement>("SettingsPaneGraphics");
            _settingsPaneAudio = tree.Q<VisualElement>("SettingsPaneAudio");
            _settingsPaneControls = tree.Q<VisualElement>("SettingsPaneControls");
            _settingsPaneNetwork = tree.Q<VisualElement>("SettingsPaneNetwork");

            // Серверы
            _serverItemHades = tree.Q<Button>("ServerItemHades");
            _serverItemTartarus = tree.Q<Button>("ServerItemTartarus");
            _serverItemCyber = tree.Q<Button>("ServerItemCyber");
            _confirmServerButton = tree.Q<Button>("ConfirmServerButton");

            if (_loaderContainer != null)
            {
                _loaderContainer.pickingMode = PickingMode.Ignore;
                _loaderContainer.style.display = DisplayStyle.None;
            }

            if (_loaderContent != null)
            {
                _loaderContent.style.display = DisplayStyle.None;
            }

            if (_modalOverlay != null)
            {
                _modalOverlay.style.display = DisplayStyle.None;
            }
        }










        protected void Update()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (Application.isPlaying && !_built)
            {
                OnEnable();
                if (!_built)
                {
                    return;
                }
            }

            // Enter Play Mode Options (Reload Domain/Scene disabled) re-creates
            // the UIDocument's panel on play entry WITHOUT re-running OnEnable:
            // the tree built at scene load stays attached to the old, disposed
            // panel, and the fresh root remains empty. The panel itself is alive
            // (it renders a separate probe panel fine), so this reads as a black
            // screen - not a rendering failure. Rebuild when our tree is no
            // longer attached to any panel. (Hot-reload is already handled in
            // OnEnable; this covers the panel-recreation case.)
            if (Application.isPlaying && _built && _doc != null && _tree != null)
            {
                // The live root is whatever the UIDocument currently exposes;
                // on play entry the panel is re-created and the old root (where
                // our tree still hangs) is replaced by a fresh empty one.
                var liveRoot = _doc.rootVisualElement;
                if (liveRoot == null || !ReferenceEquals(_tree.parent, liveRoot))
                {
                    // Clear _built alongside _tree. This is a deliberate invalidation, and
                    // leaving _built set made OnEnable report it as an unexplained
                    // hot-reload desync — a warning, with a full native stack trace, on
                    // every single play-mode entry, describing a state this line just
                    // created on purpose one statement earlier.
                    _tree = null;
                    _built = false;
                    OnEnable();
                    return;
                }
            }

            if (_loadingActive || _dismissedForServerWindow)
            {
                if (_loadingActive)
                {
                    UpdateLoaderProgress();
                }

                DismissDescentIfServerWindowOpened();
            }

            // Обе текстуры переприсваиваются каждый кадр, а не один раз при
            // сборке UI: обе живут в RenderTexture, которые пересоздаются при
            // смене разрешения окна, и старая ссылка после этого указывает на
            // уничтоженный объект.
            _sceneryPresenter.Tick(ref _spaceBgTexture);
            HandleKeyboardInput();
        }

        /// <summary>
        /// Server windows (e.g. the auth window) open in the MainGame UIDocument, which
        /// renders BELOW this menu's fullscreen layer. If the descent layer stays visible,
        /// the window is invisible and unclickable — the game looks frozen on "connecting".
        /// The moment a window is open, yield the whole layer. When the window closes,
        /// resume the descent loader until OnWorldLoaded.
        /// </summary>
        private void DismissDescentIfServerWindowOpened()
        {
            var handler = Session?.TryResolve<PacketHandler>();
            bool hasServerWindow = handler != null && handler.TopWindowTag != null;

            if (hasServerWindow)
            {
                if (!_dismissedForServerWindow)
                {
                    _dismissedForServerWindow = true;
                    _loadingActive = false;
                    HideLoader();

                    if (_tree != null)
                    {
                        _tree.style.display = DisplayStyle.None;
                        _tree.pickingMode = PickingMode.Ignore;
                        Debug.Log("[MainMenu] Fullscreen layer hidden for server window");
                    }

                    if (_root != null)
                    {
                        _root.pickingMode = PickingMode.Ignore;
                    }
                }
            }
            else if (_dismissedForServerWindow)
            {
                if (_gameManager == null || !_gameManager.IsWorldLoaded)
                {
                    _dismissedForServerWindow = false;
                    _loadingActive = true;
                    if (_tree != null)
                    {
                        _tree.style.display = DisplayStyle.Flex;
                        _tree.pickingMode = PickingMode.Position;
                    }

                    if (_root != null)
                    {
                        _root.pickingMode = PickingMode.Position;
                    }

                    if (_loaderContainer != null)
                    {
                        _loaderContainer.style.display = DisplayStyle.Flex;
                    }

                    if (_loaderContent != null)
                    {
                        _loaderContent.style.display = DisplayStyle.Flex;
                    }

                    UpdateLoaderProgress();
                    Debug.Log("[MainMenu] Server window closed, resuming descent loader");
                }
            }
        }






        private void HandleKeyboardInput()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_activeModal != null)
                {
                    CloseCurrentModal();
                }
                else if (_loadingActive && !_dismissedForServerWindow)
                {
                    CancelDescent();
                }
            }
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (_activeModal == null && !_loadingActive)
                {
                    OnPlayButtonClicked();
                }
            }
        }

        private void UpdateLoaderProgress()
        {
            _loaderProgress?.UpdateProgress(Session);
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            // Основные кнопки
            if (_playButton != null)
            {
                _playButton.clicked += OnPlayButtonClicked;
            }

            if (_serverSelectButton != null)
            {
                _serverSelectButton.clicked += () => OpenModal(_serverBrowserModal);
            }

            if (_updateAlertBanner != null)
            {
                _updateAlertBanner.clicked += () => OpenModal(_updateModal);
            }

            if (_userPillButton != null)
            {
                _userPillButton.clicked += () => OpenModal(_profileModal);
            }

            if (_cancelDescentButton != null)
            {
                _cancelDescentButton.clicked += CancelDescent;
            }

            // Сайдбар
            if (_sideChronicleButton != null)
            {
                _sideChronicleButton.clicked += () => OpenModal(_chronicleModal);
            }

            if (_sideSettingsButton != null)
            {
                _sideSettingsButton.clicked += () => OpenModal(_settingsModal);
            }

            if (_sideRepairButton != null)
            {
                _sideRepairButton.clicked += () => OpenModal(_repairModal);
            }

            if (_sideUpdateButton != null)
            {
                _sideUpdateButton.clicked += () => OpenModal(_updateModal);
            }

            if (_sideDiscordButton != null)
            {
                _sideDiscordButton.clicked += OpenDiscord;
            }

            if (_sideTelegramButton != null)
            {
                _sideTelegramButton.clicked += OpenTelegram;
            }

            if (_sideVkButton != null)
            {
                _sideVkButton.clicked += OpenVk;
            }

            if (_sideExitButton != null)
            {
                _sideExitButton.clicked += QuitGame;
            }

            // Футер
            if (_newsTickerButton != null)
            {
                _newsTickerButton.clicked += () => OpenModal(_chronicleModal);
            }

            if (_footerVersionButton != null)
            {
                // Версия берётся из настроек плеера, а не из строки в разметке.
                // Захардкоженная «ВЕРСИЯ 0.8.14» не менялась от сборки к сборке,
                // то есть по экрану нельзя было понять, какой билд запущен.
                _footerVersionButton.text = Application.isEditor
                    ? $"ВЕРСИЯ {Application.version} (РЕДАКТОР)"
                    : Debug.isDebugBuild
                        ? $"ВЕРСИЯ {Application.version} (DEV)"
                        : $"ВЕРСИЯ {Application.version}";

                _footerVersionButton.clicked += () => OpenModal(_updateModal);
            }

            // Модалки: закрытие
            BindModalClose("CloseServerModalButton");
            BindModalClose("CloseSettingsModalButton");
            BindModalClose("CloseChronicleModalButton");
            BindModalClose("CloseChronicleFooterButton");
            BindModalClose("CloseRepairModalButton");
            BindModalClose("ConfirmRepairButton");
            BindModalClose("CloseProfileModalButton");
            BindModalClose("CloseProfileFooterButton");
            BindModalClose("CloseUpdateModalButton");

            // Настройки табы
            if (_settingsTabGraphics != null)
            {
                _settingsTabGraphics.clicked += () => SwitchSettingsTab(_settingsTabGraphics, _settingsPaneGraphics);
            }

            if (_settingsTabAudio != null)
            {
                _settingsTabAudio.clicked += () => SwitchSettingsTab(_settingsTabAudio, _settingsPaneAudio);
            }

            if (_settingsTabControls != null)
            {
                _settingsTabControls.clicked += () => SwitchSettingsTab(_settingsTabControls, _settingsPaneControls);
            }

            if (_settingsTabNetwork != null)
            {
                _settingsTabNetwork.clicked += () => SwitchSettingsTab(_settingsTabNetwork, _settingsPaneNetwork);
            }

            // Серверы
            if (_serverItemHades != null)
            {
                _serverItemHades.clicked += () => SelectServer("HADES-ALPHA (EU NORTH) · 32 MS", _serverItemHades);
            }

            if (_serverItemTartarus != null)
            {
                _serverItemTartarus.clicked += () => SelectServer("TARTARUS-02 (EU CENTRAL) · 44 MS", _serverItemTartarus);
            }

            if (_serverItemCyber != null)
            {
                _serverItemCyber.clicked += () => SelectServer("CYBER-PROSPECTORS (US EAST) · 118 MS", _serverItemCyber);
            }

            if (_confirmServerButton != null)
            {
                _confirmServerButton.clicked += () =>
                {
                    CloseCurrentModal();
                    OnPlayButtonClicked();
                };
            }

            var saveSettingsBtn = _tree?.Q<Button>("SaveSettingsButton");
            if (saveSettingsBtn != null)
            {
                saveSettingsBtn.clicked += CloseCurrentModal;
            }

            var applyUpdateBtn = _tree?.Q<Button>("ApplyUpdateButton");
            if (applyUpdateBtn != null)
            {
                applyUpdateBtn.clicked += () =>
                {
                    CloseCurrentModal();
                    OnPlayButtonClicked();
                };
            }

            _modalOverlay?.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _modalOverlay)
                {
                    CloseCurrentModal();
                }
            });

            _subscribed = true;
        }

        private void BindModalClose(string buttonName)
        {
            var btn = _tree?.Q<Button>(buttonName);
            if (btn != null)
            {
                btn.clicked += CloseCurrentModal;
            }
        }

        protected void OnDisable()
        {
            // НЕ сбрасываем _subscribed: обработчики уже навешаны на живые элементы
            // дерева. Сброс флага при повторном OnEnable заставил бы SubscribeEvents
            // добавить второй экземпляр каждого клика (двойное срабатывание). Дерево
            // живёт, пока жив MainMenu; полную очистку делает OnDestroy.
        }

        public void OpenModal(VisualElement? modal)
        {
            if (modal == null || _modalOverlay == null)
            {
                return;
            }

            HideAllModals();
            _modalOverlay.style.display = DisplayStyle.Flex;
            modal.style.display = DisplayStyle.Flex;
            _activeModal = modal;
        }

        public void CloseCurrentModal()
        {
            if (_modalOverlay != null)
            {
                _modalOverlay.style.display = DisplayStyle.None;
            }

            HideAllModals();
            _activeModal = null;
        }

        private void HideAllModals()
        {
            if (_serverBrowserModal != null)
            {
                _serverBrowserModal.style.display = DisplayStyle.None;
            }

            if (_settingsModal != null)
            {
                _settingsModal.style.display = DisplayStyle.None;
            }

            if (_chronicleModal != null)
            {
                _chronicleModal.style.display = DisplayStyle.None;
            }

            if (_repairModal != null)
            {
                _repairModal.style.display = DisplayStyle.None;
            }

            if (_profileModal != null)
            {
                _profileModal.style.display = DisplayStyle.None;
            }

            if (_updateModal != null)
            {
                _updateModal.style.display = DisplayStyle.None;
            }
        }

        private void SwitchSettingsTab(Button tabBtn, VisualElement? targetPane)
        {
            _settingsTabGraphics?.RemoveFromClassList("mm-nav-tab--active");
            _settingsTabAudio?.RemoveFromClassList("mm-nav-tab--active");
            _settingsTabControls?.RemoveFromClassList("mm-nav-tab--active");
            _settingsTabNetwork?.RemoveFromClassList("mm-nav-tab--active");

            if (_settingsPaneGraphics != null)
            {
                _settingsPaneGraphics.style.display = DisplayStyle.None;
            }

            if (_settingsPaneAudio != null)
            {
                _settingsPaneAudio.style.display = DisplayStyle.None;
            }

            if (_settingsPaneControls != null)
            {
                _settingsPaneControls.style.display = DisplayStyle.None;
            }

            if (_settingsPaneNetwork != null)
            {
                _settingsPaneNetwork.style.display = DisplayStyle.None;
            }

            tabBtn.AddToClassList("mm-nav-tab--active");
            if (targetPane != null)
            {
                targetPane.style.display = DisplayStyle.Flex;
            }
        }

        private void SelectServer(string serverName, Button serverCard)
        {
            _serverItemHades?.RemoveFromClassList("mm-server-card--active");
            _serverItemTartarus?.RemoveFromClassList("mm-server-card--active");
            _serverItemCyber?.RemoveFromClassList("mm-server-card--active");

            serverCard.AddToClassList("mm-server-card--active");

        }

        private void QuitGame()
        {
            Debug.Log("[MainMenu] Exiting game client...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/fodinae");
        }

        private static void OpenTelegram()
        {
            Application.OpenURL("https://t.me/fodinae");
        }

        private static void OpenVk()
        {
            Application.OpenURL("https://vk.com/fodinae");
        }

        private void RebindGameManager()
        {
            if (Session == null)
            {
                if (_gameManager != null)
                {
                    _gameManager.OnWorldLoaded -= OnWorldLoaded;
                    _gameManager = null;
                }

                return;
            }

            GameManager? current = Session.TryResolve<GameManager>();
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

        protected void OnDestroy()
        {
            _descentCancellation?.Cancel();
            _descentCancellation?.Dispose();
            _descentCancellation = null;

            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void HideLoader()
        {
            if (_loaderContainer != null)
            {
                _loaderContainer.style.display = DisplayStyle.None;
            }
        }

        private void HideMenu()
        {
            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.None;
            }
        }

        private void OnWorldLoaded()
        {
            if (_teardownStarted)
            {
                return;
            }

            CommitLoadedWorldAsync().Forget();
        }

        private async UniTaskVoid CommitLoadedWorldAsync()
        {
            _teardownStarted = true;
            _loadingActive = false;

            // Scene unload completes asynchronously. Stop both off-screen HDR
            // renderers now, before the first gameplay frame is presented.
            // Otherwise the planet camera and starfield blit keep consuming a
            // full render pass behind the game until unload finally completes.
            _sceneryPresenter.ShutdownRenderers();

            // Маршрут доводится до конца: раньше третий шаг не подсвечивался
            // никогда, и полоса внизу навсегда застревала на «СПУСК».
            _routeDescent?.RemoveFromClassList("mm-route-item--active");
            _routeSurface?.AddToClassList("mm-route-item--active");

            HideLoader();
            HideMenu();

            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            await _sceneCoordinator.CommitStagedAsync(destroyCancellationToken);
        }

        private void OnPlayButtonClicked()
        {
            Debug.Log($"[Probe] T0 {UnityEngine.Time.realtimeSinceStartup:F3}");
            Debug.Log("[MainMenu] Play button clicked - initiating descent sequence");

            HideMenu();
            CloseCurrentModal();
            _dismissedForServerWindow = false;
            _loadingActive = true;
            _descentCancellation?.Dispose();
            _descentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                destroyCancellationToken);

            // Freeze the already rendered menu backdrop before MainGame starts
            // staging. No off-screen camera or starfield blit may compete with
            // the gameplay RenderLoop during the overlap window.
            _sceneryPresenter.ShutdownRenderers();

            if (_loaderContainer != null)
            {
                // LoaderContainer скрыт display:none по умолчанию (UXML inline + USS .mm-loader).
                // Показ только LoaderContent бесполезен — родитель остаётся невидимым, и весь
                // экран спуска (включая шкалу прогресса) не отображается.
                _loaderContainer.style.display = DisplayStyle.Flex;
            }

            if (_loaderContent != null)
            {
                _loaderContent.style.display = DisplayStyle.Flex;
            }

            _routeOrbit?.RemoveFromClassList("mm-route-item--active");
            _routeDescent?.AddToClassList("mm-route-item--active");

            _sceneryPresenter.DescentTarget = 1f;
            UpdateLoaderProgress();

            LoadGameSceneAsync().Forget();
        }

        private void CancelDescent()
        {
            Debug.Log("[MainMenu] Descent sequence canceled by user");
            _loadingActive = false;
            _descentCancellation?.Cancel();
            HideLoader();

            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.Flex;
            }

            _routeDescent?.RemoveFromClassList("mm-route-item--active");
            _routeSurface?.RemoveFromClassList("mm-route-item--active");
            _routeOrbit?.AddToClassList("mm-route-item--active");

            // Отмена — не мгновенный возврат, а тот же полёт в обратную сторону.
            _sceneryPresenter.ResumeRenderers();
            _sceneryPresenter.DescentTarget = 0f;
            _sceneCoordinator.DiscardStagedAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid LoadGameSceneAsync()
        {
            CancellationToken cancellationToken = _descentCancellation?.Token ?? destroyCancellationToken;
            await _sceneCoordinator.StageAsync(GameSceneName, cancellationToken);

            _gameManager = await WaitForGameManagerAsync(cancellationToken);
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
            _gameManager.OnWorldLoaded += OnWorldLoaded;

            var connectionService = Session?.TryResolve<IConnectionService>() ?? throw new InvalidOperationException(
                "[MainMenu] Connection service is required after the game scene loads.");
            if (!connectionService.IsConnected)
            {
                connectionService.Connect(oldClient: false);
            }
        }

        /// <summary>
        /// Waits for the game scope to finish building and hand over its <see cref="GameManager"/>.
        /// </summary>
        /// <remarks>
        /// LoadSceneAsync completing does not mean the loaded scene's LifetimeScope
        /// has built its container. VContainer defers Build through
        /// LifetimeScope.AwakeScheduler, and it is a build callback that points
        /// SessionContainer.Current at the game container — so resolving on the
        /// very next line is a race.
        ///
        /// Losing that race used to throw, and the throw landed before the
        /// OnWorldLoaded subscription below. Nothing then ever raised
        /// OnWorldLoaded, so the world never finished loading AND the menu scene,
        /// whose teardown hangs off that same event, stayed resident for the rest
        /// of the session — with its planet rig still rendering behind the game.
        /// One missed frame cost the whole frame budget.
        ///
        /// Still throws on timeout: a scope that has not appeared after several
        /// seconds is a real failure, and swallowing it would leave the menu up
        /// with no explanation.
        /// </remarks>
        private async UniTask<GameManager> WaitForGameManagerAsync(
            System.Threading.CancellationToken cancellationToken)
        {
            float deadline = Time.realtimeSinceStartup + GameScopeReadyTimeoutSeconds;
            while (true)
            {
                GameManager? candidate = Session?.TryResolve<GameManager>();
                if (candidate != null)
                {
                    return candidate;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    throw new InvalidOperationException(
                        "[MainMenu] GameManager did not become resolvable within " +
                        $"{GameScopeReadyTimeoutSeconds:F0}s of '{GameSceneName}' loading. " +
                        "The game LifetimeScope failed to build.");
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }
    }
}
