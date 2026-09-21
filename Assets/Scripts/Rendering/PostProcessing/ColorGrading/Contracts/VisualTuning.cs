#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing
{
    public static class PostProcessLook
    {
        public static class Bloom
        {
            public const float Intensity = 0.35f;
            public const float Threshold = 1.1f;
            public const float SoftKnee = 0.5f;
            public const float Radius = 1.5f;
            public const float Scatter = 0.35f;

            public static Color Tint => Color.white;
        }

        public static class Vignette
        {
            public const float Intensity = 0.28f;
            public const float Smoothness = 0.6f;

            public static Color Color => new(0f, 0f, 0f, 1f);

            public static Vector2 Center => new(0.5f, 0.5f);
        }

        public static class ColorGrading
        {
            public const float Exposure = 0f;
            public const float Contrast = 0f;
            public const float Saturation = 1f;

            public static Color Filter => Color.white;
        }

        public static class DisplayCalibration
        {
            public const float PaperWhiteNits = 350f;
            public const float PeakBrightnessNits = 1300f;
        }

        public static class SurfaceLook
        {
            public static Vector2 FlowScale => new(12f, 10f);
            public const float ShimmerSpeedScale = 0.05f;
            public const float PulseSpeedScale = 0.5f;

            public static Color ShimmerColor => Color.white;

            public static Color TransitEmissionColor => Color.white;
            public const float TransitEmissionStrength = 0.35f;

            public static Color PerspectiveEmissionColor => Color.white;
            public const float PerspectiveEmissionStrength = 0.12f;

            public const float SurfaceOccupancy = 1f;
        }

        public static class Effects
        {
            public const bool Bloom = false;
            public const bool Vignette = false;
            public const bool Eigengrau = false;
            public const bool MotionBlur = false;
        }

        public static class Grade
        {
            public const DisplayTransform Transform = DisplayTransform.None;
            public const float WhitePoint = 1f;
            public const float Temperature = 0f;
            public const float Tint = 0f;

            public static Vector3 Slope => Vector3.one;

            public static Vector3 Offset => Vector3.zero;

            public static Vector3 Power => Vector3.one;

            public const float GreyOut = 0.18f;
            public const float ShoulderPower = 4f;
            public const float ToePower = 1.6f;
            public const float ToeStops = 12f;

            // Сжатие гамута на выводе (ACES RGC). По умолчанию выключено: оно
            // единственное держало проход дисплея в кадре при нулевых эффектах,
            // а полноэкранный проход на 3420×1890 стоит десятки fps.
            public const bool GamutCompressionEnabled = false;
            public const float GamutCompressionStrength = 1f;
        }

        public static class FilmGrain
        {
            // Сила подъёма чёрной точки к цвету эйгенграу. Единица означает
            // «чёрный ровно на уровне собственного серого», то есть сам
            // эффект целиком; это не громкость зерна.
            public const float Intensity = 1f;

            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            // 60 — новый узор каждый кадр: шум зрения не держит кадр.
            // Мигало раньше не от частоты, а от амплитуды: зерно прибавлялось
            // к кадру в линейных величинах и качало его на десятки уровней
            // вывода. Теперь шум модулирует только пол в несколько уровней,
            // и обновление раз в кадр читается как зернистость, а не как
            // мельтешение. Меньшие значения удерживают узор по нескольку
            // кадров.
            public const float AnimationSpeed = 60f;

            // #16161D: собственный серый глаза, чуть холоднее нейтрали.
            // Числа — уровни кодирования, ровно те, что показывает пипетка;
            // в линейные их переводит шейдер, на единственном шаге, где это
            // уместно. Здесь пересчитывать нельзя: константа перестанет
            // читаться как цвет.
            public static Color Color => new(0.0863f, 0.0863f, 0.1137f, 1f);
        }

        public static class MotionBlur
        {
            public const float Intensity = 0.25f;
        }

    }
}

namespace Kern.World.Lighting
{
    [System.Flags]
    public enum LightingFeatureFlags
    {
        None = 0,
        StaticRC = 1 << 0,
        DynamicLights = 1 << 1,
        VisibilityAwareMerge = 1 << 3,
        WallAwareUpsample = 1 << 4,
        All = StaticRC | DynamicLights | VisibilityAwareMerge | WallAwareUpsample,
    }

    public static class LightingConfigHolder
    {
        public static LightingFeatureFlags EnabledFeatures { get; set; } =
            LightingFeatureFlags.StaticRC |
            LightingFeatureFlags.DynamicLights |
            LightingFeatureFlags.VisibilityAwareMerge |
            LightingFeatureFlags.WallAwareUpsample;

        // Общая экспозиция сцены. Одно число, на которое умножается весь свет:
        // и заполняющий, и прямой, и эмиссия. Соотношения между ними не
        // меняются — меняется только то, где вся картина стоит относительно
        // белой точки дисплея.
        //
        // Яркость нельзя чинить эмиссией. Эмиссия поднимает только источники,
        // то есть ровно то, что и так лежит выше белого и всё равно будет
        // сжато выводом: кадр от неё не светлеет, а источники выжигаются.
        // Темноту двигает экспозиция, и двигать её надо здесь, у источника
        // величин, а не грейдом на выводе: грейд стоит полноэкранного прохода,
        // а здесь это тот же умножитель, что уже уходит в шейдер.
        //
        // Единица — исходная авторская калибровка. При ней даже полностью
        // освещённая поверхность не доходила до белой точки, выше единицы
        // жили только сами источники, и вся работа вывода — плавное сжатие
        // пересвета — не начиналась вовсе: сжимать было нечего. Штатные два
        // поднимают сцену на стоп, после чего освещённое доходит до белого,
        // а пересвет попадает туда, где SDR его свернёт, а HDR покажет.
        // Это единственная ручка общей яркости; крутить её и только её.
        //
        // Свойство, а не константа: ручка вынесена в инструменты (F1, окно
        // «Цвет и вывод»), и подбирать экспозицию надо глазом на живой сцене.
        // Значение здесь — штатное; инструмент меняет его на сессию, файл
        // остаётся авторским источником правды.
        public const float DefaultSceneExposureScale = 2.0f;

        public static float SceneExposureScale { get; set; } = DefaultSceneExposureScale;

        // Базовые величины — авторская калибровка при экспозиции 1. Наружу
        // отдаются уже помноженными: потребители читают их каждый кадр, и
        // поворот ручки виден сразу, без пересборки.
        public const float BaseAmbientIntensity = 0.25f;
        public const float BaseEmissionScale = 10.0f;
        public const float BaseDynamicLightIntensity = 1.0f;
        public const float BaseMaximumLightMultiplier = 8.0f;

        public static float AmbientIntensity => BaseAmbientIntensity * SceneExposureScale;
        public static float EmissionScale => BaseEmissionScale * SceneExposureScale;
        public static readonly Color AmbientColor = Color.white;
        // Per RGB channel: sigma = ExtinctionRGB * ExtinctionMultiplier.
        // Transmission after d cells = exp(-sigma * d); multiply incoming light by it.
        // sigma: 0 = transparent; 0.2 = 81.87% per cell; 4.60517 = 1% per cell.
        // Solid affects transmission through the wall, not illumination of its front surface.
        public static readonly Color EmptyExtinctionRGB = Color.white;
        public static readonly Color SolidExtinctionRGB = Color.white;
        public const float EmptyExtinctionMultiplier = 0.20f;
        public const float SolidExtinctionMultiplier = 1.0f;

        // Стеля exposure-зебры (вид 9): всё выше — згорить і після тонмаппа.
        // Шкала в стопах від білого: 8.0 = +3 стопи. Контент HDR by design
        // (емісія до EmissionScale), тому стеля 1.0 фарбувала червоним весь
        // робочий HDR-запас.
        // Масштабируется вместе со сценой: это потолок в тех же величинах,
        // и без множителя ложная раскраска показывала бы пересвет там, где
        // его нет.
        public static float MaximumLightMultiplier =>
            BaseMaximumLightMultiplier * SceneExposureScale;

        public static bool DynamicLightEnabled => (EnabledFeatures & LightingFeatureFlags.DynamicLights) != 0;
        public static float DynamicLightIntensity =>
            BaseDynamicLightIntensity * SceneExposureScale;
        public static readonly Color DynamicLightColor = Color.white;
    }
}

namespace Kern.World.Terrain
{
    public static class TerrainLook
    {
        public const float AmbientOcclusionStrength = 1f;

        // Пол, ниже которого контактное затенение не опускает поверхность.
        //
        // Без него множитель уходил в ноль: у клетки пола вплотную к массиву
        // занятость в выборке близка к единице, и пол становился чёрным, а
        // не затенённым. След выборки в полклетки размазывал это в тёмную
        // полосу вдоль каждой границы массива, и полоса ездила вместе с
        // искажением — поле занятости пишется из смещённого силуэта.
        //
        // 0.51 — не подбор на глаз. В оригинале тень на полу это 1 - z² при
        // z = 0.7, то есть ровно 0.51, и глубже пол там не темнеет никогда.
        public const float AmbientOcclusionFloor = 0.51f;

        private static readonly int _AmbientOcclusionStrengthID =
            Shader.PropertyToID("_TerrainAmbientOcclusionStrength");

        private static readonly int _AmbientOcclusionFloorID =
            Shader.PropertyToID("_TerrainAmbientOcclusionFloor");

        // Кайма включена по умолчанию. Публикуется на старте, потому что
        // глобаль живёт в нативной части: до первого ApplyClientConfig она
        // была бы нулём, и кайма молча не рисовалась бы.
        private static readonly int _ReliefRimEnabledID =
            Shader.PropertyToID("_TerrainReliefRimEnabled");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyShaderGlobals()
        {
            Shader.SetGlobalFloat(_AmbientOcclusionStrengthID, AmbientOcclusionStrength);
            Shader.SetGlobalFloat(_AmbientOcclusionFloorID, AmbientOcclusionFloor);
            Shader.SetGlobalFloat(_ReliefRimEnabledID, 1f);
        }
    }
}
