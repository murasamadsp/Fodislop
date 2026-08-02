#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Rendering
{
    public enum GraphicsQualityTier
    {
        Low,
        Medium,
        High,
        Ultra,
    }

    [Serializable]
    public struct GraphicsQualitySettings
    {
        [Min(1)]
        public int LightingPixelsPerCell;
        [Min(128)]
        public int LightingMaximumTextureDimension;
        [Min(1)]
        public int LightingMaximumLightCount;
        [Min(1)]
        public int LightingMaximumRaySteps;
        [Min(1f)]
        public float LightingUpdatesPerSecond;
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color EmptyExtinctionRgb;
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color SolidExtinctionRgb;
        [Range(0f, 2f)]
        public float BounceStrength;
        [Min(128)]
        public int LightingCascadeAtlasLimit;
        [Range(0.5f, 1f)]
        public float RenderScale;
        [Range(0, 4)]
        public int VSyncCount;
        [Range(0, 8)]
        public int AntiAliasing;

        public GraphicsQualitySettings(
            int lightingPixelsPerCell,
            int lightingMaximumTextureDimension,
            int lightingMaximumLightCount,
            int lightingMaximumRaySteps,
            float lightingUpdatesPerSecond,
            Color? emptyExtinctionRgb = null,
            Color? solidExtinctionRgb = null,
            float bounceStrength = 0.3f,
            int lightingCascadeAtlasLimit = 512,
            float renderScale = 1f,
            int vSyncCount = 1,
            int antiAliasing = 0)
        {
            LightingPixelsPerCell = lightingPixelsPerCell;
            LightingMaximumTextureDimension = lightingMaximumTextureDimension;
            LightingMaximumLightCount = lightingMaximumLightCount;
            LightingMaximumRaySteps = lightingMaximumRaySteps;
            LightingUpdatesPerSecond = lightingUpdatesPerSecond;
            EmptyExtinctionRgb = emptyExtinctionRgb ?? new Color(0.015f, 0.012f, 0.009f, 1f);
            SolidExtinctionRgb = solidExtinctionRgb ?? new Color(4.5f, 4.25f, 4f, 1f);
            BounceStrength = bounceStrength;
            LightingCascadeAtlasLimit = lightingCascadeAtlasLimit;
            RenderScale = renderScale;
            VSyncCount = vSyncCount;
            AntiAliasing = antiAliasing;
        }
    }

    [CreateAssetMenu(fileName = "GraphicsQualityProfile", menuName = "Fodinae/Graphics Quality Profile")]
    public sealed class GraphicsQualityProfile : ScriptableObject
    {
        [SerializeField]
        private GraphicsQualitySettings _low = new(1, 512, 128, 20, 60f, lightingCascadeAtlasLimit: 512);
        [SerializeField]
        private GraphicsQualitySettings _medium = new(2, 768, 256, 28, 60f, lightingCascadeAtlasLimit: 768);
        [SerializeField]
        private GraphicsQualitySettings _high = new(2, 1024, 512, 40, 60f, lightingCascadeAtlasLimit: 1024);
        [SerializeField]
        private GraphicsQualitySettings _ultra = new(4, 1536, 1024, 64, 60f, lightingCascadeAtlasLimit: 1536);

        public GraphicsQualitySettings Get(GraphicsQualityTier tier)
        {
            GraphicsQualitySettings settings = tier switch
            {
                GraphicsQualityTier.Low => _low,
                GraphicsQualityTier.Medium => _medium,
                GraphicsQualityTier.High => _high,
                _ => _ultra,
            };

            ApplyRadianceCascadeDefaults(ref settings, tier);
            return settings;
        }

        private static void ApplyRadianceCascadeDefaults(
            ref GraphicsQualitySettings settings,
            GraphicsQualityTier tier)
        {
            (settings.LightingPixelsPerCell, settings.LightingMaximumTextureDimension) = tier switch
            {
                GraphicsQualityTier.Low => (1, 512),
                GraphicsQualityTier.Medium => (2, 768),
                GraphicsQualityTier.High => (2, 1024),
                _ => (4, 1536),
            };
            settings.LightingCascadeAtlasLimit = settings.LightingMaximumTextureDimension;
            settings.LightingUpdatesPerSecond = 60f;
            if (settings.EmptyExtinctionRgb.maxColorComponent <= 0f)
            {
                settings.EmptyExtinctionRgb = new Color(0.015f, 0.012f, 0.009f, 1f);
            }

            if (settings.SolidExtinctionRgb.maxColorComponent <= 0f)
            {
                settings.SolidExtinctionRgb = new Color(4.5f, 4.25f, 4f, 1f);
            }

            if (settings.BounceStrength <= 0f)
            {
                settings.BounceStrength = 0.3f;
            }
        }
    }
}
