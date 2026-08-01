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
            float renderScale = 1f,
            int vSyncCount = 1,
            int antiAliasing = 0)
        {
            LightingPixelsPerCell = lightingPixelsPerCell;
            LightingMaximumTextureDimension = lightingMaximumTextureDimension;
            LightingMaximumLightCount = lightingMaximumLightCount;
            LightingMaximumRaySteps = lightingMaximumRaySteps;
            LightingUpdatesPerSecond = lightingUpdatesPerSecond;
            RenderScale = renderScale;
            VSyncCount = vSyncCount;
            AntiAliasing = antiAliasing;
        }
    }

    [CreateAssetMenu(fileName = "GraphicsQualityProfile", menuName = "Fodinae/Graphics Quality Profile")]
    public sealed class GraphicsQualityProfile : ScriptableObject
    {
        [SerializeField]
        private GraphicsQualitySettings _low = new(1, 512, 128, 20, 20f);
        [SerializeField]
        private GraphicsQualitySettings _medium = new(2, 768, 256, 28, 24f);
        [SerializeField]
        private GraphicsQualitySettings _high = new(4, 1536, 512, 40, 30f);
        [SerializeField]
        private GraphicsQualitySettings _ultra = new(8, 2048, 1024, 64, 30f);

        public GraphicsQualitySettings Get(GraphicsQualityTier tier)
        {
            return tier switch
            {
                GraphicsQualityTier.Low => _low,
                GraphicsQualityTier.Medium => _medium,
                GraphicsQualityTier.High => _high,
                _ => _ultra,
            };
        }
    }
}
