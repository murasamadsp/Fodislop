#nullable enable

using Fodinae.Core;
using UnityEngine;

// ЕДИНСТВЕННОЕ МЕСТО, ГДЕ ЖИВУТ ЧИСЛА ВИДА.
//
// Раньше их было три: вид кадра в PostProcessLook, физика света в
// LightingConfigHolder и окклюзия прямо в Terrain.shader статическими
// константами. Подобрать вид значило править три файла на двух языках, и
// шейдерный третий вдобавок требовал пересборки, чтобы сдвинуть одно число.
//
// Типы намеренно оставлены с прежними именами и в прежних пространствах имён:
// на них смотрит полторы сотни мест, и переезд не должен был превратиться в
// правку каждого. Менялся адрес файла, а не адрес чисел.
//
// ПОЧЕМУ ФАЙЛ ЛЕЖИТ В КОНТРАКТАХ, А НЕ РЯДОМ С ПОТРЕБИТЕЛЯМИ. Числа читают
// разные сборки: свет — из Fodinae.World, постпроцесс — из Fodinae.Runtime и
// Fodinae.UI. Один файл может обслуживать их всех только из сборки, на которую
// ссылаются все, а такая здесь одна — Fodinae.Contracts. Положенный в
// Fodinae.Runtime, он не виден миру вовсе: Fodinae.World на Runtime не
// ссылается и ссылаться не должен, иначе выйдет цикл. По той же причине сюда
// переехал DisplayTransform: без него не собирается Grade.
//
// Отсюда же следует, что LightingConfigHolder публичный, хотя раньше был
// internal: за границей сборки internal невидим.

namespace Fodinae.Rendering.PostProcessing
{
    /// <summary>
    /// Числа, которыми описан вид кадра.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. Раньше каждый параметр постпроцесса был отдельным ползунком в
    /// настройках и отдельным полем в `ProjectDefaults.asset`: тридцать пять
    /// ползунков на вкладке эффектов, столько же полей в конфиге, и вид кадра
    /// зависел от того, куда игрок их подвинул. Вида как решения не
    /// существовало — существовал разброс.
    ///
    /// Теперь вид — авторское решение. Игроку остаётся тумблер на эффект:
    /// включить или выключить, без промежуточных значений.
    /// </remarks>
    public static class PostProcessLook
    {
        /// <summary>Свечение ярких участков.</summary>
        public static class Bloom
        {
            public const float Intensity = 0.35f;

            /// <summary>Порог в сцен-линейных единицах: ниже — не светится.</summary>
            public const float Threshold = 1.1f;
            public const float SoftKnee = 0.5f;
            public const float Radius = 3f;
            public const float Scatter = 0.55f;

            public static Color Tint => Color.white;
        }

        /// <summary>Затемнение к краям кадра.</summary>
        public static class Vignette
        {
            public const float Intensity = 0.28f;
            public const float Smoothness = 0.6f;

            public static Color Color => new(0f, 0f, 0f, 1f);

            public static Vector2 Center => new(0.5f, 0.5f);
        }

        /// <summary>Расхождение каналов к краям — дефект объектива.</summary>
        public static class ChromaticAberration
        {
            public const float Intensity = 0.06f;
        }

        /// <summary>
        /// Цветокоррекция. Работает всегда: это не эффект, а обработка кадра.
        /// Нейтральные значения — точный no-op.
        /// </summary>
        public static class ColorGrading
        {
            public const float Exposure = PostProcessSettings.DefaultExposure;
            public const float Contrast = PostProcessSettings.DefaultContrast;
            public const float Saturation = PostProcessSettings.DefaultSaturation;

            public static Color Filter => Color.white;
        }

        /// <summary>
        /// Авторский цветовой конвейер и кривая вывода.
        /// </summary>
        /// <remarks>
        /// Самые влиятельные числа файла. Кривая съедает слабый контраст в
        /// тенях, поэтому если затенение поверхности кажется бледнее, чем
        /// задано в <c>TerrainLook</c>, править надо <c>ToePower</c>, а не силу
        /// окклюзии.
        /// </remarks>
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
            public const float CurveSlope = 1f;
            public const float ShoulderPower = 4f;
            public const float ToePower = 1.6f;
            public const float ToeStops = 12f;
            public const float PathToWhiteAmount = 0f;
            public const float PathToWhitePower = 3f;
        }

        /// <summary>Плёночное зерно в тёмных участках.</summary>
        public static class FilmGrain
        {
            public const float Intensity = 0.12f;

            /// <summary>Порог темноты в перцептивном пространстве.</summary>
            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            public const float AnimationSpeed = 60f;

            public static Color Color => new(0.018f, 0.02f, 0.028f, 1f);
        }

        /// <summary>Смаз движения.</summary>
        public static class MotionBlur
        {
            public const float Intensity = 0.25f;
        }

        /// <summary>Локальное повышение контраста — резкость без нимбов.</summary>
        public static class LocalContrast
        {
            public const float Intensity = 0.15f;
        }

        /// <summary>Оптика: грязь на линзе, анаморфные лучи, дифракция, блики.</summary>
        public static class Lens
        {
            public const float DirtIntensity = 0.12f;
            public const float DirtScale = 3f;
            public const float AnamorphicIntensity = 0.35f;
            public const float AnamorphicLength = 1.5f;
            public const float DiffractionIntensity = 0.15f;
            public const float GlintIntensity = 0.12f;
            public const float GlintThreshold = 1.2f;
        }

        /// <summary>Среда: объёмная пыль и тепловое искажение.</summary>
        public static class Atmosphere
        {
            public const float DustIntensity = 0.08f;
            public const float DustScale = 1f;
            public const float DustSpeed = 0.1f;
            public const float HeatRefractionIntensity = 0.06f;
            public const float HeatRefractionScale = 2f;
        }

        /// <summary>Физика дисплея: фосфорная маска и дизеринг.</summary>
        public static class Display
        {
            public const float PhosphorMaskIntensity = 0.08f;
            public const float DitheringIntensity = 0.5f;
        }

        /// <summary>Временное накопление: послесвечение и стабилизация света.</summary>
        public static class Temporal
        {
            public const float PersistenceIntensity = 0.15f;
            public const float PersistenceDecay = 0.85f;
            public const float LightStability = 0.35f;
        }
    }
}

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Физика света: сколько его излучается, как оно затухает и где потолок.
    /// </summary>
    public static class LightingConfigHolder
    {
        public const float AmbientIntensity = 0.0f;
        public const float EmissionScale = 8.0f;
        public static readonly Color AmbientColor = Color.white;
        public static readonly Color EmptyExtinctionRGB = Color.white;
        public static readonly Color SolidExtinctionRGB = Color.white;
        public const float EmptyExtinctionMultiplier = 1.0f;
        public const float SolidExtinctionMultiplier = 0.1f;
        public const float BounceStrength = 1.0f;
        public const float MaximumLightMultiplier = 1.0f;

        /// <summary>Порог обрыва луча. Это про скорость, а не про вид.</summary>
        public const float MinimumTransmission = 0.008f;
        public const float DynamicLightIntensity = 1.0f;
        public static readonly Color DynamicLightColor = Color.white;
    }
}

namespace Fodinae.World.Terrain
{
    /// <summary>
    /// Вид поверхности террейна: затенение окружения вокруг блоков.
    /// </summary>
    /// <remarks>
    /// ПОЧЕМУ ОТСЮДА, А НЕ ИЗ ШЕЙДЕРА. Раньше это были <c>static const</c> в
    /// Terrain.shader — числа вида, до которых нельзя дотянуться, не пересобрав
    /// шейдер, и которых не видно рядом с остальным видом. Теперь они уезжают в
    /// шейдер глобалями, а живут здесь, рядом с кривой вывода, которая влияет
    /// на них сильнее всего.
    /// </remarks>
    public static class TerrainLook
    {
        /// <summary>
        /// Радиус тени вокруг блоков, уровнем мипа поля занятости. Шкала
        /// удвоения: +1 — вдвое шире, −1 — вдвое теснее.
        /// </summary>
        public const float AmbientOcclusionMip = 1.45f;

        /// <summary>Глубина тени: 0 — нет, 1 — до черноты вплотную к блоку.</summary>
        public const float AmbientOcclusionStrength = 0.9f;

        private static readonly int _AmbientOcclusionMipId =
            Shader.PropertyToID("_TerrainAmbientOcclusionMip");
        private static readonly int _AmbientOcclusionStrengthId =
            Shader.PropertyToID("_TerrainAmbientOcclusionStrength");

        /// <summary>
        /// Отправляет числа в шейдер. Один раз при старте: величины постоянные,
        /// и обновлять их покадрово было бы тратой на ровном месте.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyShaderGlobals()
        {
            Shader.SetGlobalFloat(_AmbientOcclusionMipId, AmbientOcclusionMip);
            Shader.SetGlobalFloat(_AmbientOcclusionStrengthId, AmbientOcclusionStrength);
        }
    }
}
