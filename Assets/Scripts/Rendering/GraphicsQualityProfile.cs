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
        [ColorUsage(showAlpha: false, hdr: true)]
        [Tooltip("Legacy/profile base empty extinction. Фактическое значение задаётся на WorldLighting.")]
        public Color EmptyExtinctionRgb;
        [ColorUsage(showAlpha: false, hdr: true)]
        [Tooltip("Legacy/profile base solid extinction. Фактическое значение задаётся на WorldLighting.")]
        public Color SolidExtinctionRgb;
        [Range(0f, 2f)]
        [Tooltip("Legacy/profile bounce value. Рабочая настройка diffuse bounce находится на WorldLighting.")]
        public float BounceStrength;
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
        private GraphicsQualitySettings _high = new(4, 1536, 512, 40, 60f, lightingCascadeAtlasLimit: 1536);
        [SerializeField]
        private GraphicsQualitySettings _ultra = new(8, 2048, 1024, 64, 30f, lightingCascadeAtlasLimit: 2048);

        public GraphicsQualitySettings Get(GraphicsQualityTier tier)
        {
            GraphicsQualitySettings settings = tier switch
            {
                GraphicsQualityTier.Low => _low,
                GraphicsQualityTier.Medium => _medium,
                GraphicsQualityTier.High => _high,
                _ => _ultra,
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
                settings.BounceStrength is < 0f or > 2f ||
                settings.RenderScale is < 0.5f or > 1f ||
                settings.VSyncCount is < 0 or > 4 ||
                settings.AntiAliasing is < 0 or > 8)
            {
                throw new InvalidOperationException(
                    $"Graphics quality profile '{tier}' contains invalid quality settings.");
            }

            if (settings.EmptyExtinctionRgb.maxColorComponent <= 0f ||
                settings.SolidExtinctionRgb.maxColorComponent <= 0f)
            {
                throw new InvalidOperationException(
                    $"Graphics quality profile '{tier}' is missing explicit extinction values.");
            }
        }
    }
}
