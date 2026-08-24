#nullable enable

using System;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting.Quality;
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
        public const int MinimumLightingTextureDimension = 256;

        [FormerlySerializedAs("LightingPixelsPerCell")]
        [Min(1)]
        [Tooltip("Нижняя граница lighting-пикселей на клетку. Фактическое разрешение считается от render target базовой камеры.")]
        public int LightingMinimumPixelsPerCell;
        [Min(MinimumLightingTextureDimension)]
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
        [Tooltip("Off/PerBlock/PerPixel режим освещения. Ultra всегда принудительно PerPixel.")]
        public LightingQualityMode LightingQuality;
        [Tooltip("Full/Essential/Off объём пост-обработки. Essential выключает bloom и motion blur — самую дорогую часть стека.")]
        public PostProcessQualityMode PostProcessQuality;

        public GraphicsQualitySettings(
            int lightingPixelsPerCell,
            int lightingMaximumTextureDimension,
            int lightingMaximumLightCount,
            int lightingMaximumRaySteps,
            float lightingUpdatesPerSecond,
            int lightingCascadeAtlasLimit,
            float renderScale,
            int vSyncCount,
            int antiAliasing,
            LightingQualityMode lightingQuality = LightingQualityMode.PerBlock,
            PostProcessQualityMode postProcessQuality = PostProcessQualityMode.Full)
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
            LightingQuality = lightingQuality;
            PostProcessQuality = postProcessQuality;
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
                AntiAliasing == other.AntiAliasing &&
                LightingQuality == other.LightingQuality &&
                PostProcessQuality == other.PostProcessQuality;
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
            hash.Add(settings.LightingQuality);
            hash.Add(settings.PostProcessQuality);
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
                settings.LightingMaximumTextureDimension <
                    GraphicsQualitySettings.MinimumLightingTextureDimension ||
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

            if (!Enum.IsDefined(typeof(LightingQualityMode), settings.LightingQuality))
            {
                // A value outside the known tiers would otherwise sail
                // through here (it satisfies every check above) and only
                // fail once PauseMenu tries to index its 3-entry tier-name
                // array with it - a crash on opening Settings instead of a
                // clear error at load/apply time. Catch it at the same
                // boundary every other enum-typed config field is caught at
                // (compare ClientConfigManager's GraphicsPreset check).
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' has an undefined " +
                    $"LightingQuality value ({(int)settings.LightingQuality}).");
            }

            if (!Enum.IsDefined(typeof(PostProcessQualityMode), settings.PostProcessQuality))
            {
                // Same reasoning as the LightingQuality check above: an
                // out-of-range int satisfies every numeric check and only
                // surfaces later as an IndexOutOfRangeException in the
                // PauseMenu tier-name array.
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' has an undefined " +
                    $"PostProcessQuality value ({(int)settings.PostProcessQuality}).");
            }

            if (context == nameof(GraphicsPreset.Ultra) &&
                settings.LightingQuality != LightingQualityMode.PerPixel)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' must use {nameof(LightingQualityMode.PerPixel)} " +
                    "lighting - Ultra is locked to it.");
            }
        }
    }
}
