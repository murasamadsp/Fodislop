#nullable enable

using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Rendering
{
    public enum GraphicsPreset
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh,
        Ultra,
        Custom,
    }

    [Serializable]
    public struct GraphicsQualitySettings : IEquatable<GraphicsQualitySettings>
    {
        [FormerlySerializedAs("LightingPixelsPerCell")]
        [Min(1)]
        [Tooltip("Нижняя граница lighting-пикселей на клетку. Фактическое разрешение считается от render target базовой камеры.")]
        public int LightingMinimumPixelsPerCell;
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
            LightingMinimumPixelsPerCell = lightingPixelsPerCell;
            LightingMaximumTextureDimension = lightingMaximumTextureDimension;
            LightingMaximumLightCount = lightingMaximumLightCount;
            LightingMaximumRaySteps = lightingMaximumRaySteps;
            LightingUpdatesPerSecond = lightingUpdatesPerSecond;
            LightingCascadeAtlasLimit = lightingCascadeAtlasLimit;
            RenderScale = renderScale;
            VSyncCount = vSyncCount;
            AntiAliasing = antiAliasing;
        }

        public readonly bool Equals(GraphicsQualitySettings other)
        {
            return LightingMinimumPixelsPerCell == other.LightingMinimumPixelsPerCell &&
                LightingMaximumTextureDimension == other.LightingMaximumTextureDimension &&
                LightingMaximumLightCount == other.LightingMaximumLightCount &&
                LightingMaximumRaySteps == other.LightingMaximumRaySteps &&
                LightingUpdatesPerSecond.Equals(other.LightingUpdatesPerSecond) &&
                LightingCascadeAtlasLimit == other.LightingCascadeAtlasLimit &&
                RenderScale.Equals(other.RenderScale) &&
                VSyncCount == other.VSyncCount &&
                AntiAliasing == other.AntiAliasing;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is GraphicsQualitySettings other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return CalculateHash(this);
        }

        private static int CalculateHash(GraphicsQualitySettings settings)
        {
            HashCode hash = default;
            hash.Add(settings.LightingMinimumPixelsPerCell);
            hash.Add(settings.LightingMaximumTextureDimension);
            hash.Add(settings.LightingMaximumLightCount);
            hash.Add(settings.LightingMaximumRaySteps);
            hash.Add(settings.LightingUpdatesPerSecond);
            hash.Add(settings.LightingCascadeAtlasLimit);
            hash.Add(settings.RenderScale);
            hash.Add(settings.VSyncCount);
            hash.Add(settings.AntiAliasing);
            return hash.ToHashCode();
        }

        public static bool operator ==(
            GraphicsQualitySettings left,
            GraphicsQualitySettings right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            GraphicsQualitySettings left,
            GraphicsQualitySettings right)
        {
            return !left.Equals(right);
        }
    }

    [CreateAssetMenu(fileName = "GraphicsQualityProfile", menuName = "Fodinae/Graphics Quality Profile")]
    public sealed class GraphicsQualityProfile : ScriptableObject
    {
        public const int StandardPresetCount = (int)GraphicsPreset.Custom;

        [SerializeField]
        private GraphicsQualitySettings _veryLow;
        [SerializeField]
        private GraphicsQualitySettings _low;
        [SerializeField]
        private GraphicsQualitySettings _medium;
        [SerializeField]
        private GraphicsQualitySettings _high;
        [SerializeField]
        private GraphicsQualitySettings _veryHigh;
        [SerializeField]
        private GraphicsQualitySettings _ultra;

        public GraphicsQualitySettings Get(GraphicsPreset preset)
        {
            GraphicsQualitySettings settings = preset switch
            {
                GraphicsPreset.VeryLow => _veryLow,
                GraphicsPreset.Low => _low,
                GraphicsPreset.Medium => _medium,
                GraphicsPreset.High => _high,
                GraphicsPreset.VeryHigh => _veryHigh,
                GraphicsPreset.Ultra => _ultra,
                GraphicsPreset.Custom => throw new ArgumentException(
                    "Custom graphics settings are stored in ClientConfig, not in the immutable profile.",
                    nameof(preset)),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(preset),
                    preset,
                    "Unknown graphics preset."),
            };

            ValidateSettings(settings, preset.ToString());
            return settings;
        }

        public void Validate()
        {
            for (int index = 0; index < StandardPresetCount; index++)
            {
                _ = Get((GraphicsPreset)index);
            }
        }

        public static bool IsStandard(GraphicsPreset preset)
        {
            return preset is >= GraphicsPreset.VeryLow and <= GraphicsPreset.Ultra;
        }

        public static void ValidateSettings(
            GraphicsQualitySettings settings,
            string context)
        {
            if (settings.LightingMinimumPixelsPerCell < 1 ||
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
                    $"Graphics quality settings '{context}' contain invalid technical values.");
            }
        }
    }
}
