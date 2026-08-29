#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.UI
{
    /// <summary>
    /// Запекает процедурные поля планеты в кубические карты.
    ///
    /// ЗАЧЕМ ЭТО ВООБЩЕ ЕСТЬ
    ///
    /// Ни в PlanetSurface.shader, ни в PlanetAtmosphere.shader нет _Time.
    /// Рельеф, сеть разломов и облачная палуба — статические функции
    /// направления, и всё, что двигалось в кадре, — это камера. При этом обе
    /// функции считались заново на каждый пиксель каждого кадра: около 30
    /// выборок градиентного шума у поверхности и ещё 14 у атмосферы, по восемь
    /// хешей на выборку — порядка 350 хешей на пиксель. Замер: 12–15 мс/кадр в
    /// обзорном положении и 30–36 мс на подлёте, где диск закрывает почти весь
    /// кадр. Стоимость шла строго за закрашиваемой площадью, а не за
    /// разрешением, — то есть узким местом было именно попиксельное затенение.
    ///
    /// Запекание не упрощает ни одной формулы. Ядро исполняет тот же код из
    /// PlanetSurfaceFields.hlsl и PlanetCloudFields.hlsl, что и шейдеры в
    /// процедурной ветке, — просто один раз, а не шестьдесят раз в секунду.
    /// Освещение, терминатор, самозатенение рельефа, блики и марш атмосферы
    /// остаются попиксельными: запекается только то, что и так было
    /// неизменным.
    ///
    /// ЧТО НЕ ЗАПЕКАЕТСЯ
    ///
    /// Детальное зерно поверхности и разрыв рифтовых линий остаются
    /// процедурными. Первое — потому что оно умножено на detailFade и зависит
    /// от угла обзора, то есть функцией одного направления не является.
    /// Второе — потому что свободного канала в карте нет, а стоит оно три
    /// выборки. Итого на пиксель остаётся пять выборок шума вместо сорока
    /// четырёх.
    ///
    /// ПОЧЕМУ КУБ
    ///
    /// Вход у полей — уже направление, и кубическая карта индексируется
    /// направлением напрямую: ни шва по долготе, ни сгущения текселей у
    /// полюсов, ни обратного преобразования, в котором можно ошибиться.
    ///
    /// РАЗРЕШЕНИЕ
    ///
    /// На максимальном приближении (3.6 радиуса, поле зрения 30°) диск
    /// занимает около 1200 px, а видимая шапка — 148° дуги: 0.12° на пиксель.
    /// Тексель грани в худшем случае (центр грани) — 114.6/N градусов, значит
    /// N = 1024 даёт тексели чуть мельче пикселей и рельеф не мылится даже
    /// вплотную. Облачные поля крупнее почти на порядок, им хватает 512.
    /// </summary>
    public sealed class PlanetFieldBaker : IDisposable
    {
        private const string ComputeResourcePath = "Shaders/PlanetFieldsBake";
        private const string BakedKeyword = "PLANET_FIELDS_BAKED";

        private const int SurfaceResolution = 1024;
        private const int CloudResolution = 512;
        private const int ThreadGroupSize = 8;

        private static readonly int SurfaceFieldsId = Shader.PropertyToID("_PlanetSurfaceFields");
        private static readonly int CloudFieldsId = Shader.PropertyToID("_PlanetCloudFields");
        private static readonly int ResultId = Shader.PropertyToID("_Result");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");

        private static readonly int ContinentScaleId = Shader.PropertyToID("_ContinentScale");
        private static readonly int WarpStrengthId = Shader.PropertyToID("_WarpStrength");
        private static readonly int RidgeScaleId = Shader.PropertyToID("_RidgeScale");
        private static readonly int MountainHeightId = Shader.PropertyToID("_MountainHeight");
        private static readonly int CrackScaleId = Shader.PropertyToID("_CrackScale");

        private static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
        private static readonly int CloudWarpId = Shader.PropertyToID("_CloudWarp");
        private static readonly int CloudBandsId = Shader.PropertyToID("_CloudBands");
        private static readonly int CloudBandStrengthId = Shader.PropertyToID("_CloudBandStrength");

        private ComputeShader? _compute;
        private bool _computeMissing;

        private RenderTexture? _surfaceFields;
        private RenderTexture? _cloudFields;

        // Отпечаток параметров, по которым запечены текущие карты. Материалы
        // правятся в инспекторе, и без этой проверки правка тихо не доезжала бы
        // до картинки — художник крутил бы ползунок, глядя на старую выпечку.
        private int _bakedSignature;
        private bool _baked;

        /// <summary>Запечена ли картинка прямо сейчас (для отчётов и замеров).</summary>
        public bool IsBaked => _baked;

#if UNITY_EDITOR
        /// <summary>
        /// Принудительно вернуть шейдеры в процедурную ветку.
        ///
        /// Единственный способ убедиться, что запекание ничего не изменило:
        /// снять два кадра одной и той же сценой и сравнить их попиксельно.
        /// Держать это переключателем, а не правкой кода, дешевле — сравнение
        /// понадобится снова при каждой правке полей.
        /// </summary>
        public static bool ForceProcedural { get; set; }
#endif

        /// <summary>Сколько мегабайт видеопамяти занимают карты.</summary>
        public float MegabytesUsed => _baked
            ? ((SurfaceResolution * (long)SurfaceResolution * 6 * 8) +
               (CloudResolution * (long)CloudResolution * 6 * 8)) / (1024f * 1024f)
            : 0f;

        /// <summary>
        /// Перепекает карты, если параметры материалов изменились или карт ещё
        /// нет. Вызов дешёвый: в неизменном состоянии это сравнение одного int.
        ///
        /// Если вычислительных шейдеров на платформе нет, метод молча оставляет
        /// шейдеры в процедурной ветке — она полностью рабочая и даёт ту же
        /// картинку, только дороже.
        /// </summary>
        public void EnsureBaked(Material? surface, Material? atmosphere)
        {
            if (surface == null || atmosphere == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (ForceProcedural)
            {
                DisableBakedPath();
                return;
            }
#endif

            // Копирование по слоям обязательно: ядро пишет в массив, а
            // выбирает шейдер из куба. Без него запекать некуда.
            if (!SystemInfo.supportsComputeShaders ||
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                DisableBakedPath();
                return;
            }

            ComputeShader? compute = LoadCompute();
            if (compute == null)
            {
                DisableBakedPath();
                return;
            }

            int signature = Signature(surface, atmosphere);
            if (_baked && signature == _bakedSignature &&
                _surfaceFields != null && _surfaceFields.IsCreated() &&
                _cloudFields != null && _cloudFields.IsCreated())
            {
                return;
            }

            _surfaceFields = EnsureCube(_surfaceFields, SurfaceResolution, "PlanetSurfaceFieldsCube");
            _cloudFields = EnsureCube(_cloudFields, CloudResolution, "PlanetCloudFieldsCube");

            if (_surfaceFields == null || _cloudFields == null)
            {
                DisableBakedPath();
                return;
            }

            int surfaceKernel = compute.FindKernel("BakeSurfaceFields");
            compute.SetInt(ResolutionId, SurfaceResolution);
            compute.SetFloat(ContinentScaleId, surface.GetFloat(ContinentScaleId));
            compute.SetFloat(WarpStrengthId, surface.GetFloat(WarpStrengthId));
            compute.SetFloat(RidgeScaleId, surface.GetFloat(RidgeScaleId));
            compute.SetFloat(MountainHeightId, surface.GetFloat(MountainHeightId));
            compute.SetFloat(CrackScaleId, surface.GetFloat(CrackScaleId));
            BakeInto(compute, surfaceKernel, _surfaceFields, SurfaceResolution);

            int cloudKernel = compute.FindKernel("BakeCloudFields");
            compute.SetInt(ResolutionId, CloudResolution);
            compute.SetFloat(CloudScaleId, atmosphere.GetFloat(CloudScaleId));
            compute.SetFloat(CloudWarpId, atmosphere.GetFloat(CloudWarpId));
            compute.SetFloat(CloudBandsId, atmosphere.GetFloat(CloudBandsId));
            compute.SetFloat(CloudBandStrengthId, atmosphere.GetFloat(CloudBandStrengthId));
            BakeInto(compute, cloudKernel, _cloudFields, CloudResolution);

            // Глобальные, а не свойства материалов: планета в сцене одна, карты
            // заводит запекатель, и .mat-ассеты о них знать не обязаны — иначе
            // пришлось бы держать в них ссылку на текстуру, которой на диске
            // нет.
            Shader.SetGlobalTexture(SurfaceFieldsId, _surfaceFields);
            Shader.SetGlobalTexture(CloudFieldsId, _cloudFields);
            Shader.EnableKeyword(BakedKeyword);

            _bakedSignature = signature;
            _baked = true;
        }

        /// <summary>
        /// Считает поля ядром и раскладывает результат по граням куба.
        ///
        /// Промежуточный массив слоёв здесь не от лени. Unity не позволяет
        /// привязать кубическую RenderTexture к RWTexture2DArray и отвергает
        /// такую привязку с «mismatching output texture dimension (expected 5,
        /// got 4)»: вид для записи она строит по dimension самой текстуры.
        ///
        /// Оставить массив слоёв и выбирать грань в шейдере вручную нельзя:
        /// слои фильтруются независимо, и билинейная выборка у края слоя
        /// упирается в clamp вместо соседней грани — получаются шесть швов по
        /// большим кругам. Аппаратная кубическая выборка фильтрует через
        /// стык, поэтому конечная текстура обязана быть кубом, а массив —
        /// временный и живёт только на время запекания.
        /// </summary>
        private static void BakeInto(ComputeShader compute, int kernel, RenderTexture cube, int resolution)
        {
            var slices = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "PlanetFieldsBakeScratch",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
            };

            if (!slices.Create())
            {
                Debug.LogWarning($"[Планета] Не удалось создать промежуточный массив слоёв {resolution}².");
                return;
            }

            compute.SetTexture(kernel, ResultId, slices);

            int groups = Mathf.CeilToInt(resolution / (float)ThreadGroupSize);
            compute.Dispatch(kernel, groups, groups, 6);

            for (int face = 0; face < 6; face++)
            {
                Graphics.CopyTexture(slices, face, 0, cube, face, 0);
            }

            Release(slices);
        }

        private ComputeShader? LoadCompute()
        {
            if (_compute != null || _computeMissing)
            {
                return _compute;
            }

            _compute = Resources.Load<ComputeShader>(ComputeResourcePath);
            if (_compute == null)
            {
                _computeMissing = true;
                Debug.LogWarning(
                    $"[Планета] Не найден вычислительный шейдер Resources/{ComputeResourcePath}. " +
                    "Поля считаются процедурно — картинка та же, но кадр дороже примерно вдесятеро.");
            }

            return _compute;
        }

        private static RenderTexture? EnsureCube(RenderTexture? existing, int resolution, string name)
        {
            if (existing != null && existing.IsCreated() && existing.width == resolution)
            {
                return existing;
            }

            Release(existing);

            var cube = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = name,
                dimension = TextureDimension.Cube,

                // Запись идёт вычислительным ядром прямо в тексель (x, y, грань).
                // Отрисовка полноэкранного треугольника в каждую грань потащила
                // бы за собой отражение оси Y между графическими API — тихий
                // источник разрывов на стыках граней.
                enableRandomWrite = true,

                useMipMap = false,
                autoGenerateMips = false,

                // Билинейная фильтрация без мипов — ровно то же, что и точечный
                // расчёт функции в процедурной ветке, когда тексель мельче
                // пикселя. Мипы здесь были бы уже другой картинкой: они гасят
                // рельеф на общем плане.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };

            if (!cube.Create())
            {
                Debug.LogWarning($"[Планета] Не удалось создать кубическую карту {name} {resolution}².");
                return null;
            }

            return cube;
        }

        /// <summary>
        /// Отпечаток тех и только тех параметров, от которых зависят
        /// запекаемые поля. Остальные (цвета, освещение, пороги) считаются
        /// попиксельно и перепекать из-за них нечего.
        /// </summary>
        private static int Signature(Material surface, Material atmosphere)
        {
            var hash = new HashCode();
            hash.Add(surface.GetFloat(ContinentScaleId));
            hash.Add(surface.GetFloat(WarpStrengthId));
            hash.Add(surface.GetFloat(RidgeScaleId));
            hash.Add(surface.GetFloat(MountainHeightId));
            hash.Add(surface.GetFloat(CrackScaleId));
            hash.Add(atmosphere.GetFloat(CloudScaleId));
            hash.Add(atmosphere.GetFloat(CloudWarpId));
            hash.Add(atmosphere.GetFloat(CloudBandsId));
            hash.Add(atmosphere.GetFloat(CloudBandStrengthId));
            return hash.ToHashCode();
        }

        private void DisableBakedPath()
        {
            Shader.DisableKeyword(BakedKeyword);
            _baked = false;
        }

        public void Dispose()
        {
            // Ключевое слово глобальное и переживает выход из режима игры, а
            // текстуры — нет. Снять его обязательно, иначе шейдер продолжит
            // выбирать уже уничтоженную карту и планета станет чёрной.
            Shader.DisableKeyword(BakedKeyword);
            _baked = false;

            Release(_surfaceFields);
            Release(_cloudFields);
            _surfaceFields = null;
            _cloudFields = null;
        }

        private static void Release(RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
