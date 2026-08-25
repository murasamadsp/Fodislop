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

        private enum LoadPhase
        {
            Handshake,
            WorldManifest,
            SpawnSync,
            TerrainMesh,
            SurfaceAssets,
            Done,
        }

        /// <summary>
        /// Направление на точку высадки в локальных координатах планеты.
        /// К ней привязаны и метка на поверхности, и наезд камеры при спуске,
        /// поэтому значение одно: разъехавшись, они показывали бы разные места.
        /// </summary>
        private static readonly Vector3 LandingSiteDirection = new(-0.48f, 0.10f, -0.87f);

        /// <summary>
        /// Длительность облёта камеры, в секундах.
        ///
        /// Анимация намеренно НЕ привязана к фазам загрузки. Фаз пять, то есть
        /// доля загрузки — ступенчатая функция: камера телепортировалась бы
        /// между пятью точками вместо движения, а на старте прыгала бы сразу
        /// на ту ступень, где загрузка уже находится. Полёт — это анимация, а
        /// не индикатор; за прогресс отвечает шкала.
        /// </summary>
        private const float DescentAnimationSeconds = 2.6f;

        private static readonly (LoadPhase Phase, string Label)[] PhaseSteps =
        {
            (LoadPhase.Handshake, "Подключение к серверу"),
            (LoadPhase.WorldManifest, "Загрузка карты мира"),
            (LoadPhase.SpawnSync, "Синхронизация позиции"),
            (LoadPhase.TerrainMesh, "Построение террейна"),
            (LoadPhase.SurfaceAssets, "Загрузка текстур"),
        };

        [SerializeField]
        private Texture2D? _shadeTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private VisualElement? _loaderContainer;
        private VisualElement? _beacon;
        private VisualElement? _beaconPing;
        private VisualElement? _stationBadge;
        private VisualElement? _sidebar;
        private VisualElement? _targetReticle;
        private Image? _loaderShade;
        private Image? _planetIcon;
        private VisualElement? _loaderContent;
        private VisualElement? _loaderProgressFill;
        private Label? _loaderPhaseLabel;
        private Label? _loaderPhaseCount;
        private VisualElement? _loaderPhaseList;
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

        private readonly List<(VisualElement Item, Label Icon)> _phaseItems = new();
        private bool _loadingActive;
        private bool _dismissedForServerWindow;
        private GameManager? _gameManager;
        private bool _built;
        private bool _subscribed;
        private bool _teardownStarted;

        [Inject]
        private ISessionContainer? _session;

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

            // Тир раскладки вместо @media: класс на корне, границы и значения
            // совпадают с visual/main-menu-mirror/css/tokens.css §3.
            UiLayoutTier.Attach(tree);

            BindUiElements(tree);
            BuildPhaseList();

            // Новое дерево — подписки предыдущего экземпляра недействительны
            // (OnDisable больше не сбрасывает флаг, чтобы не дублировать клики
            // на живом дереве). Сбрасываем только здесь, при полной перестройке.
            _subscribed = false;
            SubscribeEvents();
            ApplyTextures();

            _built = true;

#if UNITY_EDITOR
            _uiBuiltAt = Time.realtimeSinceStartup;
            _uiBuiltFrame = Time.frameCount;
            _planetTimingLogged = false;
#endif
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        private Image? _spaceBgImage;
        private Image? _planetBodyImage;
        [SerializeField]
        private Texture2D? _spaceBgTexture;
        private MenuSceneryController? _scenery;
        private MenuStarfield? _starfield;
        private float _scenerySearchStartedAt = -1f;

        private void BindUiElements(VisualElement tree)
        {
            _spaceBgImage = tree.Q<Image>("SpaceBgImage");
            _planetBodyImage = tree.Q<Image>("MainMenuPlanetImage");
            _beacon = tree.Q<VisualElement>("MainMenuBeacon");
            _beaconPing = tree.Q<VisualElement>("BeaconPing");
            _stationBadge = tree.Q<VisualElement>("StationBadge");

            // Рейл иконок нужен не для управления, а как препятствие: плашка
            // станции ездит по орбите и обязана его обходить.
            _sidebar = tree.Q<VisualElement>(className: "mm-sidebar");
            _targetReticle = tree.Q<VisualElement>("TargetReticle");
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer");
            _loaderContainer = tree.Q<VisualElement>("LoaderContainer");
            _loaderShade = tree.Q<Image>("LoaderShade");
            _planetIcon = tree.Q<Image>("MainMenuPlanetIcon");
            _loaderProgressFill = tree.Q<VisualElement>("LoaderProgressFill");
            _loaderPhaseLabel = tree.Q<Label>("LoaderPhaseLabel");
            _loaderPhaseCount = tree.Q<Label>("LoaderPhaseCount");
            _loaderPhaseList = tree.Q<VisualElement>("LoaderPhaseList");
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
                Debug.LogWarning($"[MainMenu] Optional image '{debugName}' is missing from UXML ({assetPath}).");
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

            // Иконка ставится ФОНОМ, а не через Image.image: перекрасить можно
            // только background-image — свойства -unity-image-tint-color в USS
            // не существует, есть лишь -unity-background-image-tint-color.
            // Благодаря этому один белый PNG обслуживает покой, наведение и
            // акцентные варианты, а цвет живёт в таблице стилей.
            var element = _tree.Q<VisualElement>(elementName);
            if (element == null)
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture: element '{elementName}' not found in UXML tree");
                return;
            }

            Texture2D? iconTex = LoadDirectTexture(assetPath);
            if (iconTex != null)
            {
                element.style.backgroundImage = new StyleBackground(iconTex);
            }
            else
            {
                Debug.LogWarning($"[MainMenu] ApplyIconTexture('{elementName}'): texture FAILED to load from '{assetPath}'");
            }
        }


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

            _starfield = MenuStarfield.Current;

            if (_starfield != null)
            {
                float resolvedWidth = _spaceBgImage.resolvedStyle.width;
                float resolvedHeight = _spaceBgImage.resolvedStyle.height;

                // Пока раскладка не посчитана — ничего не создаём. Подстановка
                // Screen.width означала текстуру во весь экран, которую на
                // следующем кадре уничтожают и создают заново нужного размера.
                if (float.IsNaN(resolvedWidth) || resolvedWidth <= 1f ||
                    float.IsNaN(resolvedHeight) || resolvedHeight <= 1f)
                {
                    return;
                }

                float panelScale = _spaceBgImage.panel?.scaledPixelsPerPoint ?? 1f;
                _starfield.SetDisplaySize(
                    Mathf.RoundToInt(resolvedWidth * panelScale),
                    Mathf.RoundToInt(resolvedHeight * panelScale));

                if (_starfield.Texture != null && !ReferenceEquals(_spaceBgImage.image, _starfield.Texture))
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
                    Debug.LogWarning("[MainMenu] Optional 'MainMenuPlanetImage' element is missing from UXML.");
                    _scenerySearchWarned = true;
                }

                return;
            }

            // Ссылка берётся напрямую, без поиска по сцене: риг проставляет её
            // в OnEnable. Опрос раз в секунду означал, что промах первой
            // попытки стоил секунды задержки — планета появлялась заметно позже
            // остального меню.
            _scenery = MenuSceneryController.Current;

            if (_scenery == null)
            {
                if (_scenerySearchStartedAt < 0f)
                {
                    _scenerySearchStartedAt = Time.realtimeSinceStartup;
                }

                if (!_scenerySearchWarned &&
                    Time.realtimeSinceStartup - _scenerySearchStartedAt > 3f)
                {
                    Debug.LogWarning(
                        "[MainMenu] MenuSceneryController не зарегистрировался за 3 с — планета останется пустой.");
                    _scenerySearchWarned = true;
                }

                return;
            }

            _scenerySearchStartedAt = -1f;

            // Риг рисует в текстуру размером с этот элемент, поэтому размер надо
            // ему сообщить. Берётся resolvedStyle, а не значение из USS: панель
            // применяет собственное масштабирование, и правило в 860px — это не
            // 860 физических пикселей на любом экране.
            float resolvedWidth = _planetBodyImage.resolvedStyle.width;
            float resolvedHeight = _planetBodyImage.resolvedStyle.height;

            // Пока раскладка не посчитана, размера просто нет. Раньше здесь
            // подставлялся Screen.width — и текстура создавалась во весь экран,
            // чтобы на следующем кадре быть уничтоженной и созданной заново уже
            // правильного размера. Один гарантированный перезалив и пустой кадр
            // на каждом входе в меню: планета появлялась не сразу.
            if (float.IsNaN(resolvedWidth) || resolvedWidth <= 1f ||
                float.IsNaN(resolvedHeight) || resolvedHeight <= 1f)
            {
                return;
            }

            float panelScale = _planetBodyImage.panel?.scaledPixelsPerPoint ?? 1f;
            _scenery.SetDisplaySize(
                Mathf.RoundToInt(resolvedWidth * panelScale),
                Mathf.RoundToInt(resolvedHeight * panelScale));

            if (_scenery.OutputTexture != null &&
                !ReferenceEquals(_planetBodyImage.image, _scenery.OutputTexture))
            {
                _planetBodyImage.image = _scenery.OutputTexture;

#if UNITY_EDITOR
                if (!_planetTimingLogged)
                {
                    _planetTimingLogged = true;
                    Debug.Log(
                        $"[Планета] Текстура подставлена через {(Time.realtimeSinceStartup - _uiBuiltAt) * 1000f:F0} мс " +
                        $"после сборки UI, кадр {Time.frameCount - _uiBuiltFrame} от неё.");
                }
#endif
            }
        }

        /// <summary>Текущее и желаемое положение камеры: 0 — меню, 1 — точка высадки.</summary>
        private float _descentCameraProgress;
        private float _descentCameraTarget;

#if UNITY_EDITOR
        private float _uiBuiltAt;
        private int _uiBuiltFrame;
        private bool _planetTimingLogged;
#endif

        private bool _scenerySearchWarned;

        private static Texture2D? LoadDirectTexture(string assetPath)
        {
            string relativePath = assetPath.StartsWith("Assets/Textures/", StringComparison.Ordinal)
                ? assetPath.Substring("Assets/Textures/".Length)
                : (assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    ? assetPath.Substring("Assets/".Length)
                    : assetPath);

            string[] candidatePaths =
            [
                assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    ? Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length))
                    : assetPath,
                Path.Combine(Application.dataPath, "Textures", relativePath),
                Path.Combine(Application.dataPath, "Resources", "Data", "Textures", relativePath),
                Path.Combine(Application.dataPath, "..", "Resources", "Data", "Textures", relativePath),
                Path.Combine(Application.dataPath, "..", "Textures", relativePath),
                Path.Combine(Application.streamingAssetsPath, "Textures", relativePath),
                Path.Combine(Application.streamingAssetsPath, "..", "Textures", relativePath),
                Path.Combine(Application.dataPath, relativePath),
            ];

            string? absolutePath = null;
            foreach (string candidate in candidatePaths)
            {
                if (File.Exists(candidate))
                {
                    absolutePath = candidate;
                    break;
                }
            }

            if (absolutePath == null)
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
            if (_teardownStarted)
            {
                return;
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

        /// <summary>
        /// Ведёт камеру к текущей цели. Вызывается каждый кадр, а не только во
        /// время загрузки: отмена спуска обязана вернуть камеру обратно, а к
        /// этому моменту загрузка уже не активна.
        /// </summary>
        private void UpdateDescentCamera()
        {
            if (Mathf.Approximately(_descentCameraProgress, _descentCameraTarget))
            {
                return;
            }

            // Движение линейное, сглаживание живёт внутри SetDescentFraming —
            // иначе плавность накладывалась бы дважды и конец пути размазывало.
            _descentCameraProgress = Mathf.MoveTowards(
                _descentCameraProgress,
                _descentCameraTarget,
                Time.unscaledDeltaTime / DescentAnimationSeconds);

            _scenery?.SetDescentFraming(_descentCameraProgress, LandingSiteDirection);
        }

        private void AnimateAmbientScene()
        {
            float time = Time.time;

            UpdateDescentCamera();

            // The marker tracks the actual orbiting body rather than sitting at a
            // fixed spot with a decorative bob, so the label and the glowing point
            // stay the same object.
            UpdateStationMarker();
            UpdateLandingSectorMarker();

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
            if (_beacon == null)
            {
                return;
            }

            // Панель равна null, пока элемент не присоединён к дереву.
            IPanel? hostPanel = _beacon.panel;

            // Единственная точка выхода, и она всегда прячет маркер. Раньше
            // выходов было четыре, и три из них оставляли метку висеть на
            // экране — в том числе до того, как планета вообще нарисовалась.
            if (hostPanel == null ||
                !TryGetPlanetFrame(out Rect rect, out Rect image) ||
                _scenery == null ||
                !_scenery.TryGetStationViewportPosition(out Vector2 viewport))
            {
                _beacon.style.display = DisplayStyle.None;
                return;
            }

            // Подпись не прячется у края, а сдвигается в свободное место:
            // иначе станция подолгу висела бы неподписанной точкой, а подпись
            // — это и есть смысл маркера.

            Rect panel = hostPanel.visualTree.worldBound;
            float panelX = image.x + (viewport.x * image.width);
            float panelY = image.y + ((1f - viewport.y) * image.height);

            const float badgeWidth = 260f;
            const float badgeHeight = 46f;
            const float footerSafe = 56f;

            if (_stationBadge != null)
            {
                // Маркер следует за станцией, подпись — удерживается.
                //
                // Раньше сторона подписи выбиралась порогом «есть ли справа
                // место». Этого мало: станция летит по орбите через весь кадр,
                // и подпись успевала заехать и под рейл иконок, и под шапку.
                // Переключение стороны на границе к тому же срабатывало каждый
                // кадр — отсюда дрожание.
                //
                // Теперь подпись просто зажимается в свободную зону. Решение
                // непрерывное, а не двоичное, поэтому переключаться нечему:
                // подпись плавно упирается в границу и скользит вдоль неё.
                const float edgeGap = 24f;
                const float markerGap = 28f;
                const float headerSafe = 84f;

                float safeRight = panel.width - edgeGap;
                if (_sidebar != null)
                {
                    Rect rail = _sidebar.worldBound;
                    if (rail.width > 0f && panelY + badgeHeight > rail.yMin && panelY < rail.yMax)
                    {
                        safeRight = Mathf.Min(safeRight, rail.xMin - edgeGap);
                    }
                }

                // Сторона по-прежнему выбирается по месту, но теперь это лишь
                // предпочтение: итог всё равно проходит через ограничение.
                float preferred = panelX + markerGap + badgeWidth <= safeRight
                    ? panelX + markerGap
                    : panelX - markerGap - badgeWidth;

                float left = Mathf.Clamp(preferred, edgeGap, Mathf.Max(edgeGap, safeRight - badgeWidth));
                float top = Mathf.Clamp(
                    panelY - (badgeHeight * 0.5f),
                    headerSafe,
                    Mathf.Max(headerSafe, panel.height - footerSafe - badgeHeight));

                // Смещение считается от маркера: подпись лежит внутри него.
                _stationBadge.style.left = left - panelX;
                _stationBadge.style.top = top - panelY;
                _stationBadge.style.right = StyleKeyword.Auto;
                _stationBadge.style.bottom = StyleKeyword.Auto;
            }

            _beacon.style.display = DisplayStyle.Flex;

            if (_beaconPing != null)
            {
                float pingPhase = (Time.time * 0.4f) % 1.0f;
                float pingScale = Mathf.Lerp(1.0f, 2.5f, pingPhase);
                float pingAlpha = Mathf.Sin(pingPhase * Mathf.PI) * 0.8f;
                _beaconPing.style.scale = new Scale(new Vector2(pingScale, pingScale));
                _beaconPing.style.opacity = pingAlpha;
            }

            // Viewport Y is bottom-up, UI Toolkit Y is top-down.
            float x = rect.x + (viewport.x * rect.width);
            float y = rect.y + ((1f - viewport.y) * rect.height);

            // Half of .mm-target-reticle's 22px box - the station marker is now
            // literally the same element as the landing reticle.
            const float markerHalfSize = 11f;
            _beacon.style.left = x - markerHalfSize;
            _beacon.style.top = y - markerHalfSize;
        }

        /// <summary>
        /// Готов ли кадр планеты, чтобы к нему можно было привязывать метки.
        ///
        /// Это ровно то условие, из-за отсутствия которого метки появлялись
        /// раньше самой планеты: риг ещё не найден, текстуры ещё нет, раскладка
        /// ещё не посчитана — а «СЕКТОР-09» уже висит в углу экрана. Проверка
        /// одна на всех потребителей, чтобы они не могли разойтись во мнениях.
        /// </summary>
        private bool TryGetPlanetFrame(out Rect localFrame, out Rect worldFrame)
        {
            localFrame = default;
            worldFrame = default;

            if (_planetBodyImage == null || _scenery == null || _scenery.OutputTexture == null)
            {
                return false;
            }

            // Текстура должна быть не просто создана, а уже подставлена в
            // элемент: между этими двумя событиями планеты на экране ещё нет.
            if (!ReferenceEquals(_planetBodyImage.image, _scenery.OutputTexture))
            {
                return false;
            }

            Rect rect = _planetBodyImage.layout;
            if (rect.width <= 1f || rect.height <= 1f ||
                float.IsNaN(rect.width) || float.IsNaN(rect.height))
            {
                return false;
            }

            // Отдаём обе системы координат сразу: метка ставится в координатах
            // родителя, а решение о свободном месте принимается в координатах
            // панели. Пока их доставали порознь, легко было перепутать.
            localFrame = rect;
            worldFrame = _planetBodyImage.worldBound;
            return true;
        }

        private void UpdateLandingSectorMarker()
        {
            if (_targetReticle == null)
            {
                return;
            }

            if (!TryGetPlanetFrame(out Rect rect, out _) ||
                _scenery == null ||
                !_scenery.TryGetPlanetSurfaceViewportPosition(LandingSiteDirection, out Vector2 viewport))
            {
                // Прятать, а не молча выходить. Раньше ранний выход оставлял
                // метку там, где её застали, — то есть в углу кадра до того,
                // как планета вообще нарисовалась.
                _targetReticle.style.display = DisplayStyle.None;
                return;
            }

            float x = rect.x + (viewport.x * rect.width);
            float y = rect.y + ((1f - viewport.y) * rect.height);

            const float markerHalfSize = 11f;
            _targetReticle.style.left = x - markerHalfSize;
            _targetReticle.style.top = y - markerHalfSize;
            _targetReticle.style.display = DisplayStyle.Flex;
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

            TerrainRenderer? terrain = Session.TryResolve<TerrainRenderer>();
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

            float progress = Mathf.Clamp01((float)phaseIndex / totalPhases);

            if (_loaderProgressFill != null)
            {
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
            if (_teardownStarted)
            {
                return;
            }

            _teardownStarted = true;
            _loadingActive = false;

            // Scene unload completes asynchronously. Stop both off-screen HDR
            // renderers now, before the first gameplay frame is presented.
            // Otherwise the planet camera and starfield blit keep consuming a
            // full render pass behind the game until unload finally completes.
            if (_scenery != null)
            {
                _scenery.gameObject.SetActive(false);
            }

            if (_starfield != null)
            {
                _starfield.gameObject.SetActive(false);
            }

            // Маршрут доводится до конца: раньше третий шаг не подсвечивался
            // никогда, и полоса внизу навсегда застревала на «СПУСК».
            _routeDescent?.RemoveFromClassList("mm-route-item--active");
            _routeSurface?.AddToClassList("mm-route-item--active");

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

            _descentCameraTarget = 1f;
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
            _routeSurface?.RemoveFromClassList("mm-route-item--active");
            _routeOrbit?.AddToClassList("mm-route-item--active");

            // Отмена — не мгновенный возврат, а тот же полёт в обратную сторону.
            _descentCameraTarget = 0f;
        }

        private async UniTaskVoid LoadGameSceneAsync()
        {
            AsyncOperation? loadOp = SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                throw new InvalidOperationException($"Failed to start loading scene '{GameSceneName}'.");
            }

            await loadOp.ToUniTask();

            _gameManager = await WaitForGameManagerAsync(destroyCancellationToken);
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
