#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        private Camera? _sceneryCamera;
        private OrbitalStationMotion? _station;
        private Transform? _planet;
        private Transform? _occluder;

        /// <summary>
        /// Обзорная дистанция камеры. Подобрана так, чтобы диск занимал около
        /// 80% высоты кадра — это и есть размер планеты из дизайн-кода.
        /// </summary>
        private const float RestDistance = 6.9f;

        /// <summary>
        /// Где стоит центр планеты по ширине кадра в обзорном положении.
        /// Больше 1.0 означало бы центр за краем экрана; 0.88 оставляет диск
        /// подрезанным правым краем, но по большей части видимым.
        /// </summary>
        private const float RestCentreFraction = 0.88f;

        /// <summary>
        /// Ближняя дистанция в радиусах планеты.
        ///
        /// Это одновременно и композиция, и производительность. Стоимость кадра
        /// здесь пропорциональна закрашиваемой площади: поверхность и объёмная
        /// атмосфера считаются попиксельно, а оболочка атмосферы вдобавок
        /// покрывает больше экрана, чем сама сфера. Замер: при 2.1 радиуса
        /// планета закрывала весь кадр и давала 8–16 FPS против 70–80 в обзоре
        /// при том же разрешении рендера.
        ///
        /// 3.6 радиуса — диск занимает около 0.9 высоты кадра: подлёт всё ещё
        /// читается, а площадь закраски меньше примерно в 2.7 раза.
        /// </summary>
        private const float CloseDistanceInRadii = 3.6f;

        /// <summary>
        /// На сколько пикселей должен измениться размер, чтобы имело смысл
        /// пересоздавать текстуры.
        /// </summary>
        private const int ResizeThresholdPixels = 24;

        private const float RestFieldOfView = 36f;
        private const float CloseFieldOfView = 30f;

        /// <summary>
        /// Насколько путь камеры выгибается влево. Ноль превратил бы облёт
        /// обратно в подъезд по прямой.
        /// </summary>
        private const float SweepDegrees = 38f;

        private int _targetWidth = 512;
        private int _targetHeight = 512;

        private RenderTexture? _cameraTarget;
        private RenderTexture? _outputTexture;

        [SerializeField]
        private Material? _resolveMaterialAsset;

        private Material? _resolveMaterial;
        private bool _ownsResolveMaterial;

        // Запекатель статических полей планеты. Живёт здесь, потому что здесь же
        // живут оба материала, которые их потребляют. Подробности — в
        // PlanetFieldBaker.
        private readonly PlanetFieldBaker _fieldBaker = new();
        private Material? _surfaceMaterial;
        private Material? _atmosphereMaterial;

        // Последнее заданное кадрирование. Хранится, потому что его нужно уметь
        // пересчитать: угол отворота камеры выводится из соотношения сторон
        // кадра, а оно меняется при каждом пересоздании текстуры.
        private float _framingProgress;
        private Vector3 _framingDirection = Vector3.back;

        /// <summary>
        /// Действующий риг задника меню.
        ///
        /// Раньше потребители искали его опросом FindAnyObjectByType раз в
        /// секунду. Если первая попытка приходилась на момент, когда сцена ещё
        /// грузится, планета появлялась на секунду позже всего остального —
        /// ровно на длину интервала опроса. Риг заявляет о себе сам, и ждать
        /// больше нечего.
        /// </summary>
        public RenderTexture? OutputTexture => _outputTexture;

        public void SetDisplaySize(int width, int height)
        {
            int w = Mathf.Max(width, 64);
            int h = Mathf.Max(height, 64);

            // Пересоздание пары RenderTexture — не бесплатная операция, а
            // размер приходит сюда из Update каждый кадр и дрожит на пиксель
            // от округлений раскладки. Точное сравнение размеров означало бы
            // перезалив на каждое такое дрожание: просадка кадра и пустая
            // планета до ближайшей отрисовки. Порог убирает это, оставаясь
            // много меньше видимой разницы в чёткости.
            if (_cameraTarget != null &&
                Mathf.Abs(_cameraTarget.width - w) <= ResizeThresholdPixels &&
                Mathf.Abs(_cameraTarget.height - h) <= ResizeThresholdPixels)
            {
                return;
            }

            _targetWidth = w;
            _targetHeight = h;

            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

            EnsureTargets();

            // Свежая текстура пуста до ближайшего LateUpdate. Без немедленной
            // отрисовки кадр после каждого изменения размера показывал дыру на
            // месте планеты.
            ResolveOutput();
        }

        private void EnsureTargets()
        {
            if (_cameraTarget == null)
            {
                _cameraTarget = new RenderTexture(_targetWidth, _targetHeight, 16, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT_Premultiplied",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _cameraTarget.Create();

                if (_sceneryCamera != null)
                {
                    _sceneryCamera.targetTexture = _cameraTarget;
                    _sceneryCamera.ResetAspect();
                    _sceneryCamera.ResetProjectionMatrix();

                    // Кадрирование пересчитывается ОБЯЗАТЕЛЬНО.
                    //
                    // Отворот камеры считается из соотношения сторон кадра, а
                    // здесь оно только что изменилось. В OnEnable текстура
                    // создаётся размером 512×512, то есть с аспектом 1.0, и
                    // угол выходит 13.9° вместо нужных 22.8° для 16:9. Без
                    // пересчёта планета оставалась стоять по углу для квадрата,
                    // и её положение зависело от того, успел ли кадр
                    // пересоздаться, — то есть выглядело случайным.
                    SetDescentFraming(_framingProgress, _framingDirection);
                }
            }

            if (_outputTexture == null)
            {
                _outputTexture = new RenderTexture(_targetWidth, _targetHeight, 0, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    anisoLevel = 0,
                };
                _outputTexture.Create();
            }
        }

        private void OnEnable()
        {
            _sceneryCamera = GetComponentInChildren<Camera>(includeInactive: true);
            _station = GetComponentInChildren<OrbitalStationMotion>(includeInactive: true);
            _planet = transform.Find("PlanetSurface");

            if (_planet != null)
            {
                _planet.localPosition = Vector3.zero;
                var atmo = transform.Find("PlanetAtmosphere");
                if (atmo != null)
                {
                    atmo.localPosition = Vector3.zero;
                }
            }

            _occluder = transform.Find("PlanetAtmosphere") ?? _planet;

            _surfaceMaterial = FindMaterial(_planet);
            _atmosphereMaterial = FindMaterial(transform.Find("PlanetAtmosphere"));
            _fieldBaker.EnsureBaked(_surfaceMaterial, _atmosphereMaterial);

            if (_sceneryCamera == null)
            {
                return;
            }

            EnsureTargets();

            if (_resolveMaterial == null)
            {
                if (_resolveMaterialAsset != null)
                {
                    _resolveMaterial = _resolveMaterialAsset;
                }
                else
                {
                    Shader? resolve = Shader.Find("Fodinae/UI/UnpremultiplyAlpha");
                    if (resolve == null)
                    {
                        Debug.LogWarning("[MenuSceneryController] Resolve shader 'Fodinae/UI/UnpremultiplyAlpha' is unavailable; scenery compositing is disabled.");
                    }
                    else
                    {
                        _resolveMaterial = new Material(resolve) { hideFlags = HideFlags.HideAndDontSave };
                        _ownsResolveMaterial = true;
                    }
                }
            }

            _sceneryCamera.allowHDR = true;
            _sceneryCamera.fieldOfView = RestFieldOfView;
            _sceneryCamera.ResetAspect();
            _sceneryCamera.ResetProjectionMatrix();
            SetDescentFraming(0f, Vector3.back);
            if (_cameraTarget != null)
            {
                _sceneryCamera.targetTexture = _cameraTarget;
                _sceneryCamera.Render();
                ResolveOutput();
            }
        }

        /// <summary>
        /// Пересобирает запечённые поля прямо сейчас.
        ///
        /// Обычно это делает LateUpdate, но в редакторе он у [ExecuteAlways]
        /// срабатывает только на перерисовке, а инструменты захвата рисуют
        /// камеру напрямую. Без явного вызова снимок «до» и снимок «после»
        /// уходили бы в один и тот же режим.
        /// </summary>
        public void RefreshFields() => _fieldBaker.EnsureBaked(_surfaceMaterial, _atmosphereMaterial);

        public void ResolveOutput()
        {
            EnsureTargets();
            if (_cameraTarget == null || _outputTexture == null || _resolveMaterial == null)
            {
                return;
            }

            Graphics.Blit(_cameraTarget, _outputTexture, _resolveMaterial);
        }

        private void LateUpdate()
        {
            // Проверка стоит сравнения одного int, пока параметры материалов не
            // менялись. Она здесь, а не только в OnEnable, ради инспектора:
            // иначе правка ползунка рельефа тихо не доезжала бы до картинки.
            _fieldBaker.EnsureBaked(_surfaceMaterial, _atmosphereMaterial);

            // The scene camera is permanently disabled by the application camera
            // authority. Render the menu target explicitly so it never joins URP's
            // screen camera loop or survives into gameplay as a second active camera.
            if (_sceneryCamera != null && _cameraTarget != null)
            {
                _sceneryCamera.Render();
            }

            ResolveOutput();
        }

        private void OnDisable()
        {
            if (_sceneryCamera != null)
            {
                _sceneryCamera.targetTexture = null;
            }
        }

        private void OnDestroy()
        {
            _fieldBaker.Dispose();

            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

            // Only destroy the fallback instance this component created; the
            // serialized asset must not be destroyed.
            if (_resolveMaterial != null && _ownsResolveMaterial)
            {
                if (Application.isPlaying)
                {
                    Destroy(_resolveMaterial);
                }
                else
                {
                    DestroyImmediate(_resolveMaterial);
                }
            }

            _resolveMaterial = null;
            _ownsResolveMaterial = false;
        }

        /// <summary>Материал первого рендерера под указанным узлом, либо null.</summary>
        private static Material? FindMaterial(Transform? node)
        {
            if (node == null)
            {
                return null;
            }

            var renderer = node.GetComponent<Renderer>();
            return renderer != null ? renderer.sharedMaterial : null;
        }

        /// <summary>
        /// Освобождает текстуру, предварительно отцепив её от камеры.
        ///
        /// Порядок значим. Уничтожение RenderTexture, которая ещё назначена в
        /// Camera.targetTexture, даёт «Releasing render texture that is set as
        /// Camera.targetTexture!» со стеком на каждое изменение размера окна:
        /// камера остаётся с висячей ссылкой, и Unity вынуждена чинить это за
        /// нас. Метод перестал быть статическим именно ради доступа к камере.
        /// </summary>
        private void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (_sceneryCamera != null && ReferenceEquals(_sceneryCamera.targetTexture, texture))
            {
                _sceneryCamera.targetTexture = null;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }

            texture = null;
        }

        /// <summary>
        /// Кадрирование спуска: камера подъезжает от обзорной точки к точке
        /// высадки. Параметр — доля пройденной загрузки, 0 = обзор, 1 = вплотную.
        ///
        /// Планету при этом никто не вращает: точка высадки закреплена за
        /// поверхностью, и разворачивать шар под камеру означало бы, что метка
        /// на поверхности переезжает вместе с ним. Двигается камера — как и
        /// должно быть при подлёте.
        /// </summary>
        public void SetDescentFraming(float progress, Vector3 landingLocalDirection)
        {
            if (_sceneryCamera == null)
            {
                return;
            }

            _framingProgress = Mathf.Clamp01(progress);
            _framingDirection = landingLocalDirection;

            float t = _framingProgress;

            // Сглаживание на концах: линейный подъезд читается как рывок на
            // старте и обрыв на финише.
            float eased = t * t * (3f - (2f * t));

            Vector3 restDirection = Vector3.back;

            Vector3 landingDirection = landingLocalDirection.sqrMagnitude > 0.0001f
                ? landingLocalDirection.normalized
                : Vector3.back;

            // Ближняя точка отсчитывается от радиуса планеты, а не задаётся
            // числом: масштаб шара в сцене менялся, и зашитая дистанция
            // однажды окажется внутри поверхности.
            float planetRadius = _planet != null ? 0.5f * _planet.lossyScale.x : 1f;
            float closeDistance = Mathf.Max(planetRadius * CloseDistanceInRadii, planetRadius + 0.35f);

            // Облёт, а не подъезд по прямой.
            //
            // Точка высадки лежит почти напротив обзорной позиции — прямая дуга
            // между ними всего около 29°, и движение читается как простой зум.
            // Поэтому путь выгибается влево промежуточной точкой: камера сперва
            // уходит в сторону, показывая планету сбоку, и только потом заходит
            // на точку. Это две последовательные сферические интерполяции —
            // построение Безье, перенесённое на сферу.
            Vector3 sweepMid = Quaternion.AngleAxis(-SweepDegrees, Vector3.up)
                * Vector3.Slerp(restDirection, landingDirection, 0.5f);

            Vector3 direction = Vector3.Slerp(
                Vector3.Slerp(restDirection, sweepMid, eased),
                Vector3.Slerp(sweepMid, landingDirection, eased),
                eased);

            // Дистанция идёт своей интерполяцией: если гнать её тем же Slerp по
            // векторам, скорость подхода зависит от кривизны дуги и на выгибе
            // камера подтормаживает.
            float distance = Mathf.Lerp(RestDistance, closeDistance, eased);
            Vector3 local = direction * distance;

            _sceneryCamera.transform.localPosition = local;

            // В обзоре камера смотрит мимо планеты — тем и достигается её
            // положение справа. К точке высадки она доворачивается точно на
            // центр, иначе на подлёте цель уезжала бы за край кадра.
            Quaternion aimAtCentre = Quaternion.LookRotation(-local.normalized, Vector3.up);
            _sceneryCamera.transform.localRotation =
                aimAtCentre * Quaternion.Euler(0f, Mathf.Lerp(-RestYaw(), 0f, eased), 0f);
            _sceneryCamera.fieldOfView = Mathf.Lerp(RestFieldOfView, CloseFieldOfView, eased);
            _sceneryCamera.ResetProjectionMatrix();
        }

        /// <summary>
        /// Угол отворота камеры в обзоре, посчитанный из текущего соотношения
        /// сторон.
        ///
        /// Зашивать его числом нельзя: угол постоянен, а горизонтальное поле
        /// зрения растёт вместе с шириной кадра — на широком экране планета
        /// поехала бы к центру, на узком ушла бы за край целиком. Считаем из
        /// доли ширины, и композиция держится на любом экране.
        /// </summary>
        private float RestYaw()
        {
            if (_sceneryCamera == null)
            {
                return 0f;
            }

            float tanHalfVertical = Mathf.Tan(RestFieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHalfHorizontal = tanHalfVertical * Mathf.Max(_sceneryCamera.aspect, 0.1f);

            // Из доли ширины в нормализованную координату кадра: 0.5 — центр, 1 — правый край.
            float normalized = (RestCentreFraction * 2f) - 1f;

            return Mathf.Atan(normalized * tanHalfHorizontal) * Mathf.Rad2Deg;
        }

        /// <summary>Возвращает камеру в обзорное положение меню.</summary>
        public void ResetFraming() => SetDescentFraming(0f, Vector3.back);

        // Reports the orbiting station's on-screen position as a 0..1 viewport
        // fraction (origin bottom-left, matching Camera.WorldToViewportPoint),
        // so UI Toolkit callers can convert it into their own panel space.
        //
        // Returns false while the station is not actually visible, so a label
        // anchored to it can be hidden rather than left hovering over the disc
        // with nothing underneath.
        public bool TryGetStationViewportPosition(out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null || _station == null)
            {
                return false;
            }

            Vector3 stationWS = _station.transform.position;
            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(stationWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            if (_occluder != null)
            {
                Vector3 cameraWS = _sceneryCamera.transform.position;
                Vector3 toPlanet = _occluder.position - cameraWS;
                Vector3 toStation = stationWS - cameraWS;

                // Occluded when the station is on the far side of the planet's
                // centre AND falls inside its silhouette. A sphere makes this a
                // cheap exact test - no depth buffer read needed.
                if (toStation.magnitude > toPlanet.magnitude)
                {
                    float radius = _occluder.lossyScale.x * 0.5f;
                    float offAxis = Vector3.ProjectOnPlane(toStation, toPlanet.normalized).magnitude;
                    if (offAxis < radius)
                    {
                        return false;
                    }
                }
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed point along the orbital ring.
        /// </summary>
        public bool TryGetOrbitPointViewportPosition(float angleDegrees, out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null)
            {
                return false;
            }

            Transform centerTransform = _planet != null ? _planet : transform;
            const float orbitRadius = 1.72f;
            var orbitTilt = new Vector3(72f, 0f, -19f);
            var localOffset = new Vector3(
                Mathf.Cos(angleDegrees * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(angleDegrees * Mathf.Deg2Rad)) * orbitRadius;
            Quaternion orbitPlane = Quaternion.Euler(orbitTilt);
            Vector3 pointWS = centerTransform.position + (orbitPlane * localOffset);

            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(pointWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed landing point on the planet's surface.
        /// </summary>
        public bool TryGetPlanetSurfaceViewportPosition(Vector3 localSurfaceDir, out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null || _planet == null)
            {
                return false;
            }

            float planetRadius = 0.5f * _planet.lossyScale.x;
            Vector3 pointWS = _planet.position + (localSurfaceDir.normalized * planetRadius);

            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(pointWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }
    }
}
