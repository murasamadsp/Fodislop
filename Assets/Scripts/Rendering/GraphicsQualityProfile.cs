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
        [Tooltip("Количество lighting-пикселей на одну физическую клетку. Выше — точнее и дороже.")]
        public int LightingPixelsPerCell;
        [Min(128)]
        [Tooltip("Максимальный размер lighting field в пикселях.")]
        public int LightingMaximumTextureDimension;
        [Min(1)]
        [Tooltip("Максимальное число dynamic light sources, загружаемых в GPU buffer.")]
        public int LightingMaximumLightCount;
        [Min(1)]
        [Tooltip("Максимальное число шагов одного cascade interval.")]
        public int LightingMaximumRaySteps;
        [Min(1f)]
        [Tooltip("Максимальная частота lighting solve. Изменение геометрии всё равно обрабатывается сразу.")]
        public float LightingUpdatesPerSecond;
        [Min(128)]
        [Tooltip("Бюджет radiance cascade atlas.")]
        public int LightingCascadeAtlasLimit;
        [Range(0.5f, 1f)]
        [Tooltip("URP render scale для данного quality tier.")]
        public float RenderScale;
        [Range(0, 4)]
        [Tooltip("Количество вертикальных синхронизаций.")]
        public int VSyncCount;
        [Range(0, 8)]
        [Tooltip("MSAA sample count для данного quality tier.")]
        public int AntiAliasing;

        public GraphicsQualitySettings(
            int lightingPixelsPerCell,
            int lightingMaximumTextureDimension,
            int lightingMaximumLightCount,
            int lightingMaximumRaySteps,
            float lightingUpdatesPerSecond,
            int lightingCascadeAtlasLimit,
            float renderScale,
            int vSyncCount,
            int antiAliasing)
        {
            LightingPixelsPerCell = lightingPixelsPerCell;
            LightingMaximumTextureDimension = lightingMaximumTextureDimension;
            LightingMaximumLightCount = lightingMaximumLightCount;
            LightingMaximumRaySteps = lightingMaximumRaySteps;
            LightingUpdatesPerSecond = lightingUpdatesPerSecond;
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
        private GraphicsQualitySettings _low = new(1, 512, 128, 20, 60f, 512, 0.75f, 1, 0);
        [SerializeField]
        private GraphicsQualitySettings _medium = new(2, 768, 256, 28, 60f, 768, 0.85f, 1, 0);
        [SerializeField]
        private GraphicsQualitySettings _high = new(4, 1536, 512, 40, 60f, 1536, 1f, 1, 0);
        [SerializeField]
        private GraphicsQualitySettings _ultra = new(4, 2048, 1024, 64, 60f, 2048, 1f, 1, 0);

        public GraphicsQualitySettings Get(GraphicsQualityTier tier)
        {
            GraphicsQualitySettings settings = tier switch
            {
                GraphicsQualityTier.Low => _low,
                GraphicsQualityTier.Medium => _medium,
                GraphicsQualityTier.High => _high,
                GraphicsQualityTier.Ultra => _ultra,
                _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown graphics quality tier."),
            };

            Validate(settings, tier);
            return settings;
        }

        private static void Validate(GraphicsQualitySettings settings, GraphicsQualityTier tier)
        {
            if (settings.LightingPixelsPerCell < 1 ||
                settings.LightingMaximumTextureDimension < 128 ||
                settings.LightingMaximumLightCount < 1 ||
                settings.LightingMaximumRaySteps < 1 ||
                settings.LightingUpdatesPerSecond <= 0f ||
                settings.LightingCascadeAtlasLimit < 128 ||
                settings.RenderScale is < 0.5f or > 1f ||
                settings.VSyncCount is < 0 or > 4 ||
                settings.AntiAliasing is < 0 or > 8)
            {
                throw new InvalidOperationException(
                    $"Graphics quality profile '{tier}' contains invalid quality settings.");
            }
        }
    }
}
