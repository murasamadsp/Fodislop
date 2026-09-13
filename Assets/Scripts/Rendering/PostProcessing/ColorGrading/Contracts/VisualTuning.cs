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

        public static class ChromaticAberration
        {
            public const float Intensity = 0.06f;
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

            // Сжатие гамута на выводе (ACES RGC): по умолчанию включено полностью.
            public const bool GamutCompressionEnabled = true;
            public const float GamutCompressionStrength = 1f;
        }

        public static class FilmGrain
        {
            public const float Intensity = 0.12f;

            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            public const float AnimationSpeed = 60f;

            public static Color Color => new(0.018f, 0.02f, 0.028f, 1f);
        }

        public static class MotionBlur
        {
            public const float Intensity = 0.25f;
        }

        public static class LocalContrast
        {
            public const float Intensity = 0.15f;
        }

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

        public static class Atmosphere
        {
            public const float DustIntensity = 0.08f;
            public const float DustScale = 1f;
            public const float DustSpeed = 0.1f;
            public const float HeatRefractionIntensity = 0.06f;
            public const float HeatRefractionScale = 2f;
        }

        public static class Display
        {
            public const float PhosphorMaskIntensity = 0.08f;
            public const float DitheringIntensity = 0.5f;
        }

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

        public const float MinimumTransmission = 0.008f;
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
