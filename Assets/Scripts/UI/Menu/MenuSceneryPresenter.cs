#nullable enable

using System;
using System.IO;
using Fodinae.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Owns the main menu's ambient scene: the starfield backdrop, the planet
    /// render target, the descent camera fly-in and the surface markers.
    ///
    /// Split out of MainMenu, which was a 1676-line MonoBehaviour. Everything
    /// here reads and writes only this class's own fields; the two texture
    /// caches stay on MainMenu because they are [SerializeField] and their
    /// serialized nature is load-bearing (see ApplyImageTexture), so they are
    /// threaded through by ref rather than copied.
    /// </summary>
    internal sealed class MenuSceneryPresenter
    {
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

        private VisualElement? _tree;
        private Image? _spaceBgImage;
        private Image? _planetBodyImage;
        private Image? _loaderShade;
        private Image? _planetIcon;
        private VisualElement? _beacon;
        private VisualElement? _beaconPing;
        private VisualElement? _stationBadge;
        private VisualElement? _sidebar;
        private VisualElement? _targetReticle;

        private MenuSceneryController? _scenery;
        private MenuStarfield? _starfield;
        private float _scenerySearchStartedAt = -1f;
        private bool _scenerySearchWarned;

        /// <summary>Текущее и желаемое положение камеры: 0 — меню, 1 — точка высадки.</summary>
        private float _descentCameraProgress;
        private float _descentCameraTarget;

#if UNITY_EDITOR
        private float _uiBuiltAt;
        private int _uiBuiltFrame;
        private bool _planetTimingLogged;
#endif

        /// <summary>Куда ведёт облёт: 0 — меню, 1 — точка высадки.</summary>
        public float DescentTarget
        {
            get => _descentCameraTarget;
            set => _descentCameraTarget = value;
        }

        /// <summary>
        /// Пересобирает текстуры и продвигает анимацию. Вызывается каждый кадр:
        /// обе текстуры живут в RenderTexture, которые пересоздаются при смене
        /// разрешения окна, и старая ссылка после этого указывает на
        /// уничтоженный объект — поэтому переприсваивание, а не разовая привязка.
        /// </summary>
        public void Tick(ref Texture2D? spaceBgTexture)
        {
            TryApplyStarfieldTexture(ref spaceBgTexture);
            TryApplySceneryTexture();
            Animate();
        }

        /// <summary>
        /// Привязывает компоненты сцены меню. Ссылки приходят из serialized
        /// контракта MainMenuLifetimeScope, а не из статической регистрации:
        /// состояние сцены не должно жить в глобальных одиночках.
        /// </summary>
        public void BindScene(MenuStarfield? starfield, MenuSceneryController? scenery)
        {
            _starfield = starfield;
            _scenery = scenery;
        }

        /// <summary>Резолвит собственные элементы из уже собранного дерева UXML.</summary>
        public void Bind(VisualElement tree)
        {
            _tree = tree;
            _spaceBgImage = tree.Q<Image>("SpaceBgImage");
            _planetBodyImage = tree.Q<Image>("MainMenuPlanetImage");
            if (_planetBodyImage != null && _scenery?.OutputTexture != null)
            {
                _planetBodyImage.image = _scenery.OutputTexture;
            }

            _loaderShade = tree.Q<Image>("LoaderShade");
            _planetIcon = tree.Q<Image>("MainMenuPlanetIcon");
            _beacon = tree.Q<VisualElement>("MainMenuBeacon");
            _beaconPing = tree.Q<VisualElement>("BeaconPing");
            _stationBadge = tree.Q<VisualElement>("StationBadge");

            // Рейл иконок нужен не для управления, а как препятствие: плашка
            // станции ездит по орбите и обязана его обходить.
            _sidebar = tree.Q<VisualElement>(className: "mm-sidebar");
            _targetReticle = tree.Q<VisualElement>("TargetReticle");
        }

        /// <summary>Отметка времени сборки UI для editor-диагностики появления планеты.</summary>
        public void MarkUIBuilt()
        {
#if UNITY_EDITOR
            _uiBuiltAt = Time.realtimeSinceStartup;
            _uiBuiltFrame = Time.frameCount;
            _planetTimingLogged = false;
#endif
        }

        /// <summary>
        /// Гасит оба закадровых HDR-рендерера. Выгрузка сцены асинхронная, и без
        /// этого камера планеты и блит звёздного поля продолжают занимать полный
        /// проход рендера позади уже идущей игры.
        /// </summary>
        public void ShutdownRenderers()
        {
            if (_scenery != null)
            {
                _scenery.gameObject.SetActive(false);
            }

            if (_starfield != null)
            {
                _starfield.gameObject.SetActive(false);
            }
        }

        public void ResumeRenderers()
        {
            if (_scenery != null)
            {
                _scenery.gameObject.SetActive(true);
            }

            if (_starfield != null)
            {
                _starfield.gameObject.SetActive(true);
            }
        }

        public void ApplyTextures(ref Texture2D? shadeTexture, ref Texture2D? spaceBgTexture)
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
            TryApplyStarfieldTexture(ref spaceBgTexture);

            TryApplySceneryTexture();

            ApplyImageTexture(_loaderShade, ref shadeTexture, "Assets/Textures/UI/mm_shade.png", nameof(_loaderShade));

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
        private void TryApplyStarfieldTexture(ref Texture2D? spaceBgTexture)
        {
            if (_spaceBgImage == null)
            {
                return;
            }

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
            ApplyImageTexture(_spaceBgImage, ref spaceBgTexture, "Assets/Textures/UI/mm_space_bg.png", nameof(_spaceBgImage));
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
        }

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

        public void Animate()
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
    }
}
