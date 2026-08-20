#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Fodinae
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        private const string GameSceneName = "MainGame";

        // USS modifiers toggled on the station badge so it flips to whichever
        // side of the marker has room instead of sliding off-screen.
        private const string StationBadgeRightClass = "mm-target-badge--right";
        private const string StationBadgeAboveClass = "mm-target-badge--above";

        private enum LoadPhase
        {
            Handshake,
            WorldManifest,
            SpawnSync,
            TerrainMesh,
            SurfaceAssets,
            Done,
        }

        private static readonly (LoadPhase Phase, string Label)[] PhaseSteps =
        {
            (LoadPhase.Handshake, "Подключение к серверу"),
            (LoadPhase.WorldManifest, "Загрузка карты мира"),
            (LoadPhase.SpawnSync, "Синхронизация позиции"),
            (LoadPhase.TerrainMesh, "Построение террейна"),
            (LoadPhase.SurfaceAssets, "Загрузка текстур"),
        };

        [SerializeField]
        private Texture2D? _loaderTexture;
        [SerializeField]
        private Texture2D? _shadeTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private VisualElement? _loaderContainer;
        private VisualElement? _beacon;
        private VisualElement? _stationBadge;
        private VisualElement? _targetReticle;
        private Image? _loaderImage;
        private Image? _loaderShade;
        private Image? _planetIcon;
        private VisualElement? _loaderContent;
        private VisualElement? _loaderProgressFill;
        private Label? _loaderPhaseLabel;
        private Label? _loaderPhaseCount;
        private VisualElement? _loaderPhaseList;
        private Label? _routeOrbit;
        private Label? _routeDescent;
        private Label? _networkLabel;

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
        private Button? _footerRepairButton;

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

        private readonly List<(VisualElement Item, Label Icon)> _phaseItems = new();
        private bool _loadingActive;
        private bool _dismissedForServerWindow;
        private GameManager? _gameManager;
        private bool _built;
        private bool _subscribed;

        // MainMenu живёт вне DI-графа (ExecuteAlways, сцена без скоупа) — текущий
        // контейнер сессии получаем через BootstrapLifetimeScope.Instance.
        //
        // НЕ кэшируем: с Enter Play Mode Options (Reload Domain/Scene disabled)
        // этот компонент и его поля переживают play-сессии, а SessionContainer —
        // объект сессии. Закэшированный экземпляр из прошлой сессии указывает на
        // выброшенный контейнер (Current мёртв), тогда как новый игровой скоуп
        // переключает Current у СВОЕГО SessionContainer. Закэшированный давал
        // TryResolve<GameManager> == null ровно после загрузки MainGame — спуск
        // умирал с «GameManager is required». Резолв каждый раз — дешёвый
        // TryResolve из живого Bootstrap-контейнера.
        private ISessionContainer? Session
        {
            get
            {
                ISessionContainer? fresh = SessionAccess.Resolve();
                if (fresh != null && fresh.Current != null)
                {
                    return fresh;
                }

                return null;
            }
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
            _doc = GetComponent<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "MainMenu requires a UIDocument with a ready rootVisualElement.");
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
                ApplyTextures();
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

            BindUiElements(tree);
            BuildPhaseList();

            // Новое дерево — подписки предыдущего экземпляра недействительны
            // (OnDisable больше не сбрасывает флаг, чтобы не дублировать клики
            // на живом дереве). Сбрасываем только здесь, при полной перестройке.
            _subscribed = false;
            SubscribeEvents();
            ApplyTextures();

            _built = true;
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        private Image? _spaceBgImage;
        private Image? _planetBodyImage;
        [SerializeField]
        private Texture2D? _spaceBgTexture;
        private MenuSceneryController? _scenery;
        private MenuStarfield? _starfield;

        private void BindUiElements(VisualElement tree)
        {
            _spaceBgImage = tree.Q<Image>("SpaceBgImage");
            _planetBodyImage = tree.Q<Image>("MainMenuPlanetImage");
            _beacon = tree.Q<VisualElement>("MainMenuBeacon");
            _stationBadge = tree.Q<VisualElement>("StationBadge");
            _targetReticle = tree.Q<VisualElement>("TargetReticle");
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer");
            _loaderContainer = tree.Q<VisualElement>("LoaderContainer");
            _loaderImage = tree.Q<Image>("LoaderImage");
            _loaderShade = tree.Q<Image>("LoaderShade");
            _planetIcon = tree.Q<Image>("MainMenuPlanetIcon");
            _loaderProgressFill = tree.Q<VisualElement>("LoaderProgressFill");
            _loaderPhaseLabel = tree.Q<Label>("LoaderPhaseLabel");
            _loaderPhaseCount = tree.Q<Label>("LoaderPhaseCount");
            _loaderPhaseList = tree.Q<VisualElement>("LoaderPhaseList");
            _loaderContent = tree.Q<VisualElement>("LoaderContent");
            _routeOrbit = tree.Q<Label>("MainMenuRouteOrbit");
            _routeDescent = tree.Q<Label>("MainMenuRouteDescent");
            _networkLabel = tree.Q<Label>("MainMenuNetworkLabel");

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
            _footerRepairButton = tree.Q<Button>("FooterRepairButton");

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

            if (_loaderImage != null)
            {
                _loaderImage.pickingMode = PickingMode.Ignore;
            }

            if (_modalOverlay != null)
            {
                _modalOverlay.style.display = DisplayStyle.None;
            }
        }

        private void ApplyTextures()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Безопасное авто-разрешение текстур без падений - every path here
            // logs on failure now; a silently-unset Image with no error is
            // exactly the failure mode that cost hours to track down once.
            // Фон космоса больше не запечённая PNG: MenuStarfield рисует
            // мерцающее звёздное поле блитом в RenderTexture, и она подставляется
            // в тот же элемент. Запечённая текстура остаётся в проекте как
            // запасной вариант, если компонента в сцене нет.
            TryApplyStarfieldTexture();

            TryApplySceneryTexture();

            ApplyImageTexture(_loaderImage, ref _loaderTexture, "Assets/Textures/loader_new.png", nameof(_loaderImage));
            ApplyImageTexture(_loaderShade, ref _shadeTexture, "Assets/Textures/UI/mm_shade.png", nameof(_loaderShade));

            Texture2D? unusedLogoCache = null;
            ApplyImageTexture(_planetIcon, ref unusedLogoCache, "Assets/Textures/UI/mm_logo.png", nameof(_planetIcon));

            ApplyIconTexture("SideChronicleIcon", "Assets/Textures/UI/mm_icon_chronicle.png");
            ApplyIconTexture("SideSettingsIcon", "Assets/Textures/UI/mm_icon_settings.png");
            ApplyIconTexture("SideRepairIcon", "Assets/Textures/UI/mm_icon_repair.png");
            ApplyIconTexture("SideUpdateIcon", "Assets/Textures/UI/mm_icon_update.png");
            ApplyIconTexture("SideDiscordIcon", "Assets/Textures/UI/mm_icon_discord.png");
            ApplyIconTexture("SideTelegramIcon", "Assets/Textures/UI/mm_icon_telegram.png");
            ApplyIconTexture("SideVkIcon", "Assets/Textures/UI/mm_icon_vk.png");
            ApplyIconTexture("SideExitIcon", "Assets/Textures/UI/mm_icon_exit.png");
        }

        private static void ApplyImageTexture(Image? image, ref Texture2D? cache, string assetPath, string debugName)
        {
            if (image == null)
            {
                Debug.LogError($"[MainMenu] {debugName} is NULL - element not found when binding UXML tree ({assetPath})");
                return;
            }

            // Explicit == null (not ??=): a [SerializeField] Texture2D can hold
            // a stale/destroyed reference after the asset was regenerated -
            // Unity's overloaded == correctly treats that as null, but ??=
            // bypasses the overload and would skip reloading it forever.
            if (cache == null)
            {
                cache = LoadDirectTexture(assetPath);
            }

            if (cache != null)
            {
                image.image = cache;
            }
            else
            {
                Debug.LogWarning($"[MainMenu] {debugName}: texture FAILED to load from '{assetPath}'");
            }
        }

        private void ApplyIconTexture(string elementName, string assetPath)
        {
            if (_tree == null)
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture('{elementName}'): _tree is null, UI not built yet");
                return;
            }

            var img = _tree.Q<Image>(elementName);
            if (img == null)
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture: element '{elementName}' not found in UXML tree");
                return;
            }

            Texture2D? iconTex = LoadDirectTexture(assetPath);
            if (iconTex != null)
            {
                img.image = iconTex;
            }
            else
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture('{elementName}'): texture FAILED to load from '{assetPath}'");
            }
        }

        private float _lastComponentSearchTime;

        // Подставляет процедурное звёздное поле в фоновый Image.
        //
        // Раньше звёзды рисовались квадом на отдельной камере прямо в дисплей.
        // Это и клало небо поверх игры: квад - обычная мировая геометрия, а
        // игровая камера рендерит все слои, пока сцена меню ещё загружена.
        private void TryApplyStarfieldTexture()
        {
            if (_spaceBgImage == null)
            {
                return;
            }

            if (_starfield == null && (Time.unscaledTime - _lastComponentSearchTime > 1f || _lastComponentSearchTime == 0f))
            {
                _lastComponentSearchTime = Time.unscaledTime;
                _starfield = UnityEngine.Object.FindAnyObjectByType<MenuStarfield>(FindObjectsInactive.Include);
            }

            if (_starfield != null && _starfield.Texture != null)
            {
                if (!ReferenceEquals(_spaceBgImage.image, _starfield.Texture))
                {
                    _spaceBgImage.image = _starfield.Texture;
                }

                return;
            }

            // Нет компонента - остаётся запечённый фон, чтобы меню не осталось
            // на пустом чёрном.
            ApplyImageTexture(_spaceBgImage, ref _spaceBgTexture, "Assets/Textures/UI/mm_space_bg.png", nameof(_spaceBgImage));
        }

        private void TryApplySceneryTexture()
        {
            if (_planetBodyImage == null)
            {
                if (!_scenerySearchWarned)
                {
                    Debug.LogError("[MainMenu] _planetBodyImage is NULL - 'MainMenuPlanetImage' element not found");
                    _scenerySearchWarned = true;
                }

                return;
            }

            if (_scenery == null && (Time.unscaledTime - _lastComponentSearchTime > 1f || _lastComponentSearchTime == 0f))
            {
                _lastComponentSearchTime = Time.unscaledTime;
                _scenery = UnityEngine.Object.FindAnyObjectByType<MenuSceneryController>(FindObjectsInactive.Include);
                if (_scenery == null && !_scenerySearchWarned && Time.time > 3f)
                {
                    Debug.LogWarning("[MainMenu] No MenuSceneryController found in the loaded scenes after 3s - planet render will stay blank");
                    _scenerySearchWarned = true;
                }
            }

            if (_scenery != null && _scenery.OutputTexture != null &&
                !ReferenceEquals(_planetBodyImage.image, _scenery.OutputTexture))
            {
                _planetBodyImage.image = _scenery.OutputTexture;
            }
        }

        private bool _scenerySearchWarned;

        private static Texture2D? LoadDirectTexture(string assetPath)
        {
            string absolutePath = assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length))
                : assetPath;
            bool exists = File.Exists(absolutePath);
            if (!exists)
            {
                return null;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(absolutePath);
                return RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                    fileData,
                    Path.GetFileNameWithoutExtension(assetPath),
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    makeNoLongerReadable: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MainMenu] Failed to load direct texture '{absolutePath}': {ex.Message}");
                return null;
            }
        }

        protected void Update()
        {
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
                    Debug.Log("[MainMenu] Visual tree no longer in the live panel root - rebuilding.");
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
            TryApplyStarfieldTexture();
            TryApplySceneryTexture();
            AnimateAmbientScene();
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

        private void AnimateAmbientScene()
        {
            float time = Time.time;

            // The marker tracks the actual orbiting body rather than sitting at a
            // fixed spot with a decorative bob, so the label and the glowing point
            // stay the same object.
            UpdateStationMarker();

            if (_targetReticle != null)
            {
                float targetScale = 1.0f + (Mathf.Sin(time * 2.2f) * 0.04f);
                _targetReticle.style.scale = new Scale(new Vector3(targetScale, targetScale, 1f));
            }

            // The planet body is deliberately NOT animated.
            //
            // It used to be drifted by sin(time) * 2px on Y as a "subtle float".
            // Two pixels is a sub-pixel offset for the render target, so every
            // frame resampled the whole highly-detailed surface at a slightly
            // different phase - the terrain crawled and the body looked like it
            // was breathing. A planet is also the one object in frame that must
            // read as immovable; its motion belongs in the orbiting station.
        }

        // Anchors the orbital-station label to where the station actually renders
        // inside the scenery RenderTexture. The viewport fraction is relative to
        // the scenery camera, and the planet Image draws that exact texture, so
        // the Image's own layout rect is the correct space to map it into -
        // using the container's rect instead would drift as soon as the two
        // differ in size.
        private void UpdateStationMarker()
        {
            if (_beacon == null || _planetBodyImage == null)
            {
                return;
            }

            if (_scenery == null || !_scenery.TryGetStationViewportPosition(out Vector2 viewport))
            {
                _beacon.style.display = DisplayStyle.None;
                return;
            }

            Rect rect = _planetBodyImage.layout;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                // Layout has not been resolved yet on the first frames.
                return;
            }

            // The badge is moved to whichever side of the marker has room rather
            // than the whole label being hidden near an edge. Hiding it meant the
            // station spent long stretches of its orbit as an unlabelled dot -
            // the label is the point of the marker, so the layout gives way, not
            // the information.
            // panel is null until the element is attached; on the first frames
            // after a rebuild this would otherwise dereference null.
            IPanel? hostPanel = _beacon.panel;
            if (hostPanel == null)
            {
                return;
            }

            Rect panel = hostPanel.visualTree.worldBound;
            Rect image = _planetBodyImage.worldBound;
            float panelX = image.x + (viewport.x * image.width);
            float panelY = image.y + ((1f - viewport.y) * image.height);

            const float badgeWidth = 260f;
            const float badgeHeight = 46f;
            const float footerSafe = 56f;

            if (_stationBadge != null)
            {
                bool roomOnRight = panelX + badgeWidth < panel.width;
                _stationBadge.EnableInClassList(StationBadgeRightClass, roomOnRight);

                // Near the footer the badge is lifted above the marker instead of
                // hanging below it and sliding under the bar.
                bool nearBottom = panelY + badgeHeight > panel.height - footerSafe;
                _stationBadge.EnableInClassList(StationBadgeAboveClass, nearBottom);
            }

            _beacon.style.display = DisplayStyle.Flex;

            // Viewport Y is bottom-up, UI Toolkit Y is top-down.
            float x = rect.x + (viewport.x * rect.width);
            float y = rect.y + ((1f - viewport.y) * rect.height);

            // Half of .mm-target-reticle's 22px box - the station marker is now
            // literally the same element as the landing reticle.
            const float markerHalfSize = 11f;
            _beacon.style.left = x - markerHalfSize;
            _beacon.style.top = y - markerHalfSize;
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

        private void BuildPhaseList()
        {
            if (_loaderPhaseList == null)
            {
                return;
            }

            _phaseItems.Clear();
            _loaderPhaseList.Clear();
            foreach ((LoadPhase _, string label) in PhaseSteps)
            {
                var item = new VisualElement();
                item.AddToClassList("mm-loader-phase-item");

                var icon = new Label("○");
                icon.AddToClassList("mm-loader-phase-icon");
                item.Add(icon);

                var text = new Label(label);
                item.Add(text);

                _loaderPhaseList.Add(item);
                _phaseItems.Add((item, icon));
            }
        }

        private LoadPhase ComputeLoadPhase()
        {
            if (Session == null)
            {
                return LoadPhase.Handshake;
            }

            IConnectionService? connectionService = Session.TryResolve<IConnectionService>();
            if (connectionService == null || !connectionService.IsConnected)
            {
                return LoadPhase.Handshake;
            }

            MapManager? mapManager = Session.TryResolve<MapManager>();
            if (mapManager == null || !mapManager.IsWorldInitialized)
            {
                return LoadPhase.WorldManifest;
            }

            PlayerMovementController? player = PlayerMovementController.LocalPlayer;
            if (player == null || !player.HasServerPosition)
            {
                return LoadPhase.SpawnSync;
            }

            Robot? robot = player.GetComponent<Robot>();
            if (robot == null || !robot.IsMetadataLoaded)
            {
                return LoadPhase.SpawnSync;
            }

            IPlayerStats? stats = Session.TryResolve<IPlayerStats>();
            if (stats == null || !stats.IsReady)
            {
                return LoadPhase.SpawnSync;
            }

            TerrainRenderer? terrain = TerrainRenderer.Instance;
            if (terrain == null || !terrain.IsReadyForGameplay)
            {
                return LoadPhase.TerrainMesh;
            }

            ITextureService? textureService = Session.TryResolve<ITextureService>();
            IAssetLoader? assetLoader = Session.TryResolve<IAssetLoader>();
            bool assetsBusy = (textureService != null && textureService.PendingCellTextureRequests > 0) ||
                (assetLoader is ClientAssetLoader clientAssetLoader &&
                    (clientAssetLoader.PendingAssetCount > 0 || clientAssetLoader.QueuedAssetCount > 0));
            if (assetsBusy)
            {
                return LoadPhase.SurfaceAssets;
            }

            return LoadPhase.Done;
        }

        private void UpdateLoaderProgress()
        {
            LoadPhase phase = ComputeLoadPhase();
            int phaseIndex = (int)phase;
            int totalPhases = PhaseSteps.Length;

            if (_networkLabel != null)
            {
                _networkLabel.text = phase == LoadPhase.Handshake ? "ПОДКЛЮЧЕНИЕ..." : "HADES-ALPHA · ОНЛАЙН";
            }

            if (_loaderProgressFill != null)
            {
                float progress = Mathf.Clamp01((float)phaseIndex / totalPhases);
                _loaderProgressFill.style.width = new Length(progress * 100f, LengthUnit.Percent);
            }

            if (_loaderPhaseLabel != null)
            {
                _loaderPhaseLabel.text = phaseIndex < totalPhases
                    ? PhaseSteps[phaseIndex].Label
                    : "Готово к высадке";
            }

            if (_loaderPhaseCount != null)
            {
                _loaderPhaseCount.text = $"{Mathf.Min(phaseIndex + 1, totalPhases)} / {totalPhases}";
            }

            for (int i = 0; i < _phaseItems.Count; i++)
            {
                (VisualElement item, Label icon) = _phaseItems[i];
                bool isDone = i < phaseIndex;
                bool isActive = i == phaseIndex;
                item.EnableInClassList("mm-loader-phase-item--done", isDone);
                item.EnableInClassList("mm-loader-phase-item--active", isActive);
                icon.text = isDone ? "✓" : isActive ? "◆" : "○";
            }
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
                _footerVersionButton.clicked += () => OpenModal(_updateModal);
            }

            if (_footerRepairButton != null)
            {
                _footerRepairButton.clicked += () => OpenModal(_repairModal);
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

            if (_networkLabel != null)
            {
                _networkLabel.text = serverName;
            }
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
                Debug.Log("[MainMenu] Loader hidden");
            }
        }

        private void HideMenu()
        {
            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.None;
                Debug.Log("[MainMenu] Menu hidden");
            }
        }

        private void OnWorldLoaded()
        {
            _loadingActive = false;
            HideLoader();
            HideMenu();

            if (_tree != null)
            {
                _tree.style.display = DisplayStyle.None;
                _tree.pickingMode = PickingMode.Ignore;
                Debug.Log("[MainMenu] Fullscreen layer hidden");
            }

            if (_root != null)
            {
                _root.Clear();
                _root.pickingMode = PickingMode.Ignore;
            }

            if (_doc != null)
            {
                _doc.enabled = false;
            }

            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            SceneManager.UnloadSceneAsync(gameObject.scene).ToUniTask().Forget();
        }

        private void OnPlayButtonClicked()
        {
            Debug.Log($"[Probe] T0 {UnityEngine.Time.realtimeSinceStartup:F3}");
            Debug.Log("[MainMenu] Play button clicked - initiating descent sequence");

            HideMenu();
            CloseCurrentModal();
            _dismissedForServerWindow = false;
            _loadingActive = true;
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
            UpdateLoaderProgress();

            LoadGameSceneAsync().Forget();
        }

        private void CancelDescent()
        {
            Debug.Log("[MainMenu] Descent sequence canceled by user");
            _loadingActive = false;
            HideLoader();

            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.Flex;
            }

            _routeDescent?.RemoveFromClassList("mm-route-item--active");
            _routeOrbit?.AddToClassList("mm-route-item--active");
        }

        private async UniTaskVoid LoadGameSceneAsync()
        {
            AsyncOperation? loadOp = SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                throw new InvalidOperationException($"Failed to start loading scene '{GameSceneName}'.");
            }

            await loadOp.ToUniTask();

            _gameManager = Session?.TryResolve<GameManager>() ?? throw new InvalidOperationException(
                "[MainMenu] GameManager is required after the game scene loads.");
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
            _gameManager.OnWorldLoaded += OnWorldLoaded;

            var connectionService = Session?.TryResolve<IConnectionService>() ?? throw new InvalidOperationException(
                "[MainMenu] Connection service is required after the game scene loads.");
            if (!connectionService.IsConnected)
            {
                connectionService.Connect(oldClient: false);
            }
        }
    }
}
