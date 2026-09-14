#nullable enable

using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing
{
    public static class PostProcessLook
    {
        public static class Bloom
        {
            public const float Intensity = 0.35f;

            public const float Threshold = 1.1f;
            public const float SoftKnee = 0.5f;
            public const float Radius = 3f;
            public const float Scatter = 0.55f;

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
            public const float Exposure = PostProcessSettings.DefaultExposure;
            public const float Contrast = PostProcessSettings.DefaultContrast;
            public const float Saturation = PostProcessSettings.DefaultSaturation;

            public static Color Filter => Color.white;
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
            public const float CurveSlope = 1f;
            public const float ShoulderPower = 4f;
            public const float ToePower = 1.6f;
            public const float ToeStops = 12f;
            public const float PathToWhiteAmount = 0f;
            public const float PathToWhitePower = 3f;

            // Сжатие гамута на выводе (ACES RGC). По умолчанию выключено: оно
            // единственное держало проход дисплея в кадре при нулевых эффектах,
            // а полноэкранный проход на 3420×1890 стоит десятки fps.
            public const bool GamutCompressionEnabled = false;
            public const float GamutCompressionStrength = 1f;
        }

        public static class FilmGrain
        {
            public const float Intensity = 0.3f;

            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            public const float AnimationSpeed = 60f;

            public static Color Color => new(0.018f, 0.02f, 0.028f, 1f);
        }

        public static class MotionBlur
        {
            public const float Intensity = 0.25f;
        }

    }
}

namespace Fodinae.World.Lighting
{
    public static class LightingConfigHolder
    {
        public const float AmbientIntensity = 0.0f;
        public const float EmissionScale = 8.0f;
        public static readonly Color AmbientColor = Color.white;
        public static readonly Color EmptyExtinctionRGB = Color.white;
        public static readonly Color SolidExtinctionRGB = Color.white;
        // Поглощение пустоты на клетку. При 0.01 свет гас лишь через ~300 клеток,
        // и дальность обрезал конец последнего каскада — резкой, зависящей от
        // пресета границей. 0.1 гасит свет до ~5% за 30 клеток, до порога
        // MinimumTransmission — к ~48: затухание успевает раньше обрезки.
        public const float EmptyExtinctionMultiplier = 0.2f;
        // Поглощение на клетку. 800 — прежние 100 с множителем 8, который был
        // зашит в шейдере: картинка не меняется, число теперь честное.
        public const float SolidExtinctionMultiplier = 1600.0f;
        public const float BounceStrength = 1.0f;
        public const float MaximumLightMultiplier = 1.0f;

        public const float MinimumTransmission = 0.008f;
        public const bool DynamicLightEnabled = true;
        public const float DynamicLightIntensity = 1.0f;
        public static readonly Color DynamicLightColor = Color.white;
    }
}

namespace Fodinae.World.Terrain
{
    public static class TerrainLook
    {
        public const float AmbientOcclusionMip = 1.45f;

        public const float AmbientOcclusionStrength = 0.9f;

        private static readonly int _AmbientOcclusionMipID =
            Shader.PropertyToID("_TerrainAmbientOcclusionMip");
        private static readonly int _AmbientOcclusionStrengthID =
            Shader.PropertyToID("_TerrainAmbientOcclusionStrength");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyShaderGlobals()
        {
            Shader.SetGlobalFloat(_AmbientOcclusionMipID, AmbientOcclusionMip);
            Shader.SetGlobalFloat(_AmbientOcclusionStrengthID, AmbientOcclusionStrength);
        }
    }
}
