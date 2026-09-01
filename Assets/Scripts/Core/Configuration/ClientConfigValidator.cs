#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core;

internal sealed class ClientConfigValidator(
    IProjectDefaults projectDefaults,
    GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly IProjectDefaults _projectDefaults = projectDefaults ??
        throw new ArgumentNullException(nameof(projectDefaults));
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    /// <summary>
    /// Проверяет persisted данные без неявной подстановки defaults.
    /// </summary>
    public void Validate(ClientConfig config)
    {
        if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported client config schema {config.SchemaVersion}; " +
                $"expected {ClientConfig.CurrentSchemaVersion}.");
        }

        if (!string.Equals(
                config.ProjectDefaultsHash,
                _projectDefaults.ContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Client config ProjectDefaultsHash does not match the active ProjectDefaults snapshot.");
        }

        ValidateFloat(config.MasterVolume, 0f, 1f, nameof(config.MasterVolume));
        ValidateFloat(config.SfxVolume, 0f, 1f, nameof(config.SfxVolume));
        ValidateFloat(config.MusicVolume, 0f, 1f, nameof(config.MusicVolume));
        ValidateFloat(config.AmbienceVolume, 0f, 1f, nameof(config.AmbienceVolume));
        ValidateFloat(config.VoiceVolume, 0f, 1f, nameof(config.VoiceVolume));
        ValidateFloat(config.UIVolume, 0f, 1f, nameof(config.UIVolume));
        ValidateFloat(config.UIScale, 0.5f, 2f, nameof(config.UIScale));
        if (!Enum.IsDefined(typeof(GraphicsPreset), config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Unknown graphics preset value '{config.GraphicsPreset}'.");
        }

        try
        {
            GraphicsQualityProfile.ValidateSettings(
                config.GraphicsQualitySettings,
                config.GraphicsPreset.ToString());
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(
                "Client graphics quality settings are invalid.",
                ex);
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
            config.GraphicsQualitySettings != _graphicsQualityProfile.Get(config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' was mutated in client config.");
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
            !HasStandardGraphicsValues(config))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' contains customized visual values. " +
                "Mark the preset as Custom before changing graphics settings.");
        }

        ValidateFloat(config.AmbientIntensity, 0f, 1f, nameof(config.AmbientIntensity));
        ValidateFloat(config.EmissionScale, 0.1f, 8f, nameof(config.EmissionScale));
        ValidateColor(config.AmbientColor, nameof(config.AmbientColor));
        ValidateColor(config.EmptyExtinctionRgb, nameof(config.EmptyExtinctionRgb));
        ValidateColor(config.SolidExtinctionRgb, nameof(config.SolidExtinctionRgb));
        ValidateFloat(config.EmptyExtinctionMultiplier, 0f, 2f, nameof(config.EmptyExtinctionMultiplier));
        ValidateFloat(config.SolidExtinctionMultiplier, 0.25f, 2f, nameof(config.SolidExtinctionMultiplier));
        ValidateFloat(config.BounceStrength, 0f, 1f, nameof(config.BounceStrength));
        ValidateFloat(config.AmbientOcclusionRadiusCells, 0.5f, 8f, nameof(config.AmbientOcclusionRadiusCells));
        ValidateFloat(config.AmbientOcclusionStrength, 0.1f, 8f, nameof(config.AmbientOcclusionStrength));
        ValidateFloat(config.MaximumLightMultiplier, 0.25f, LightingConfigLimits.MaximumLightMultiplier, nameof(config.MaximumLightMultiplier));
        ValidateFloat(config.TransmittanceDebugDistanceCells, 2f, 32f, nameof(config.TransmittanceDebugDistanceCells));
        ValidateFloat(config.MinimumTransmission, 0.0001f, 0.1f, nameof(config.MinimumTransmission));
        ValidateInt(config.LightSafeBorder, 0, 8, nameof(config.LightSafeBorder));
        ValidateFloat(config.DynamicLightIntensity, 0f, 4f, nameof(config.DynamicLightIntensity));
        ValidateColor(config.DynamicLightColor, nameof(config.DynamicLightColor));
        ValidateFloat(config.DynamicLightUpdatesPerSecond, 1f, LightingConfigLimits.DynamicLightUpdatesPerSecond, nameof(config.DynamicLightUpdatesPerSecond));
        ValidateFloat(config.TerrainFlowScale.x, 0.001f, 1024f, nameof(config.TerrainFlowScale.x));
        ValidateFloat(config.TerrainFlowScale.y, 0.001f, 1024f, nameof(config.TerrainFlowScale.y));
        ValidateFloat(config.TerrainShimmerSpeedScale, 0f, 10f, nameof(config.TerrainShimmerSpeedScale));
        ValidateFloat(config.TerrainPulseSpeedScale, 0f, 10f, nameof(config.TerrainPulseSpeedScale));
        ValidateColor(config.TerrainShimmerColor, nameof(config.TerrainShimmerColor));
        ValidateColor(config.TerrainDebugColor, nameof(config.TerrainDebugColor));
        ValidateFloat(config.BloomThreshold, 0f, 2f, nameof(config.BloomThreshold));
        ValidateFloat(config.BloomSoftKnee, 0f, 1f, nameof(config.BloomSoftKnee));
        ValidateFloat(config.BloomRadius, 0.5f, 8f, nameof(config.BloomRadius));
        ValidateFloat(config.BloomScatter, 0.1f, 1f, nameof(config.BloomScatter));
        ValidateColor(config.BloomTint, nameof(config.BloomTint));
        ValidateColor(config.TransitEmissionColor, nameof(config.TransitEmissionColor));
        ValidateFloat(config.TransitEmissionStrength, 0f, 8f, nameof(config.TransitEmissionStrength));
        ValidateColor(config.PerspectiveEmissionColor, nameof(config.PerspectiveEmissionColor));
        ValidateFloat(config.PerspectiveEmissionStrength, 0f, 8f, nameof(config.PerspectiveEmissionStrength));
        ValidateFloat(config.SurfaceOccupancy, 0f, 1f, nameof(config.SurfaceOccupancy));
        ValidateFloat(config.BloomIntensity, 0f, 5f, nameof(config.BloomIntensity));
        ValidateFloat(config.VignetteIntensity, 0f, 1f, nameof(config.VignetteIntensity));
        ValidateColor(config.VignetteColor, nameof(config.VignetteColor));
        ValidateFloat(config.VignetteSmoothness, 0.01f, 1f, nameof(config.VignetteSmoothness));
        ValidateFloat(config.VignetteCenter.x, 0f, 1f, nameof(config.VignetteCenter.x));
        ValidateFloat(config.VignetteCenter.y, 0f, 1f, nameof(config.VignetteCenter.y));
        ValidateFloat(config.ChromaticAberrationIntensity, 0f, 1f, nameof(config.ChromaticAberrationIntensity));
        ValidateFloat(config.ColorGradingExposure, -4f, 4f, nameof(config.ColorGradingExposure));
        ValidateColor(config.ColorGradingFilter, nameof(config.ColorGradingFilter));
        ValidateFloat(config.ColorGradingSaturation, 0f, 2f, nameof(config.ColorGradingSaturation));
        ValidateFloat(
            config.ColorGradingToneMappingWhitePoint,
            0.25f,
            8f,
            nameof(config.ColorGradingToneMappingWhitePoint));
        ValidateFloat(config.EigengrauIntensity, 0f, 1f, nameof(config.EigengrauIntensity));
        ValidateColor(config.EigengrauColor, nameof(config.EigengrauColor));
        ValidateFloat(config.EigengrauDarknessThreshold, 0.02f, 0.75f, nameof(config.EigengrauDarknessThreshold));
        ValidateFloat(config.EigengrauNoiseScale, 0.75f, 2f, nameof(config.EigengrauNoiseScale));
        ValidateFloat(config.EigengrauAnimationSpeed, 1f, 60f, nameof(config.EigengrauAnimationSpeed));
        ValidateFloat(config.MotionBlurIntensity, 0f, 1f, nameof(config.MotionBlurIntensity));
        AdvancedPostProcessSettings advanced = config.AdvancedPostProcess ??
            throw new InvalidDataException("AdvancedPostProcess settings are missing.");
        ValidateFloat(advanced.LocalContrastIntensity, 0f, 0.5f, nameof(advanced.LocalContrastIntensity));
        ValidateFloat(advanced.LensDirtIntensity, 0f, 0.35f, nameof(advanced.LensDirtIntensity));
        ValidateFloat(advanced.LensDirtScale, 0.25f, 16f, nameof(advanced.LensDirtScale));
        ValidateFloat(advanced.AnamorphicIntensity, 0f, 1f, nameof(advanced.AnamorphicIntensity));
        ValidateFloat(advanced.AnamorphicLength, 0.25f, 8f, nameof(advanced.AnamorphicLength));
        ValidateFloat(advanced.ChromaticDiffractionIntensity, 0f, 0.5f, nameof(advanced.ChromaticDiffractionIntensity));
        ValidateFloat(advanced.HeatRefractionIntensity, 0f, 0.25f, nameof(advanced.HeatRefractionIntensity));
        ValidateFloat(advanced.HeatRefractionScale, 0.25f, 16f, nameof(advanced.HeatRefractionScale));
        ValidateFloat(advanced.GlintIntensity, 0f, 0.5f, nameof(advanced.GlintIntensity));
        ValidateFloat(advanced.GlintThreshold, 0f, 4f, nameof(advanced.GlintThreshold));
        ValidateFloat(advanced.VolumetricDustIntensity, 0f, 0.25f, nameof(advanced.VolumetricDustIntensity));
        ValidateFloat(advanced.VolumetricDustScale, 0.1f, 8f, nameof(advanced.VolumetricDustScale));
        ValidateFloat(advanced.VolumetricDustSpeed, 0f, 2f, nameof(advanced.VolumetricDustSpeed));
        ValidateFloat(advanced.PhosphorMaskIntensity, 0f, 0.35f, nameof(advanced.PhosphorMaskIntensity));
        ValidateFloat(advanced.DitheringIntensity, 0f, 1f, nameof(advanced.DitheringIntensity));
        ValidateFloat(advanced.TemporalPersistenceIntensity, 0f, 0.8f, nameof(advanced.TemporalPersistenceIntensity));
        ValidateFloat(advanced.TemporalPersistenceDecay, 0f, 0.98f, nameof(advanced.TemporalPersistenceDecay));
        ValidateFloat(advanced.LightStability, 0f, 0.9f, nameof(advanced.LightStability));
        if (string.IsNullOrWhiteSpace(config.ServerHost))
        {
            throw new InvalidDataException(
                "Client config value 'ServerHost' must be a non-empty host name or IP address.");
        }

        ValidateInt(config.ServerPort, 1, 65535, nameof(config.ServerPort));
        if (!Enum.IsDefined(typeof(FullScreenMode), config.FullScreenMode))
        {
            throw new InvalidDataException(
                $"Client config value 'FullScreenMode' must be a valid FullScreenMode value, got {config.FullScreenMode}.");
        }
    }

    private bool HasStandardGraphicsValues(ClientConfig config)
    {
        LightingDefaultsSnapshot lighting = _projectDefaults.Lighting;
        ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
        return config.AmbientOcclusionEnabled == lighting.AmbientOcclusionEnabled &&
            config.DiffuseBounceEnabled == lighting.DiffuseBounceEnabled &&
            config.AmbientIntensity == lighting.AmbientIntensity &&
            config.EmissionScale == lighting.EmissionScale &&
            config.AmbientColor == lighting.AmbientColor &&
            config.EmptyExtinctionRgb == lighting.EmptyExtinctionRgb &&
            config.SolidExtinctionRgb == lighting.SolidExtinctionRgb &&
            config.EmptyExtinctionMultiplier == lighting.EmptyExtinctionMultiplier &&
            config.SolidExtinctionMultiplier == lighting.SolidExtinctionMultiplier &&
            config.BounceStrength == lighting.BounceStrength &&
            config.AmbientOcclusionRadiusCells == lighting.AmbientOcclusionRadiusCells &&
            config.AmbientOcclusionStrength == lighting.AmbientOcclusionStrength &&
            config.MaximumLightMultiplier == lighting.MaximumLightMultiplier &&
            config.EnableFinalLightingClamp == lighting.EnableFinalLightingClamp &&
            config.TransmittanceDebugDistanceCells == lighting.TransmittanceDebugDistanceCells &&
            config.MinimumTransmission == lighting.MinimumTransmission &&
            config.LightSafeBorder == lighting.LightSafeBorder &&
            config.DynamicLightIntensity == lighting.DynamicLightIntensity &&
            config.DynamicLightColor == lighting.DynamicLightColor &&
            config.DynamicLightUpdatesPerSecond == lighting.DynamicLightUpdatesPerSecond &&
            config.TerrainFlowScale == shaders.TerrainFlowScale &&
            config.TerrainShimmerSpeedScale == shaders.TerrainShimmerSpeedScale &&
            config.TerrainPulseSpeedScale == shaders.TerrainPulseSpeedScale &&
            config.TerrainShimmerColor == shaders.TerrainShimmerColor &&
            config.TerrainDebugColor == shaders.TerrainDebugColor &&
            config.TerrainDebugMode == shaders.TerrainDebugMode &&
            config.BloomThreshold == shaders.BloomThreshold &&
            config.BloomSoftKnee == shaders.BloomSoftKnee &&
            config.BloomRadius == shaders.BloomRadius &&
            config.BloomScatter == shaders.BloomScatter &&
            config.BloomTint == shaders.BloomTint &&
            config.TransitEmissionColor == shaders.TransitEmissionColor &&
            config.TransitEmissionStrength == shaders.TransitEmissionStrength &&
            config.PerspectiveEmissionColor == shaders.PerspectiveEmissionColor &&
            config.PerspectiveEmissionStrength == shaders.PerspectiveEmissionStrength &&
            config.SurfaceOccupancy == shaders.SurfaceOccupancy &&
            config.BloomIntensity == shaders.BloomIntensity &&
            config.VignetteIntensity == shaders.VignetteIntensity &&
            config.VignetteColor == shaders.VignetteColor &&
            config.VignetteSmoothness == shaders.VignetteSmoothness &&
            config.VignetteCenter == shaders.VignetteCenter &&
            config.ChromaticAberrationIntensity == shaders.ChromaticAberrationIntensity &&
            config.ColorGradingExposure == shaders.ColorGradingExposure &&
            config.ColorGradingFilter == shaders.ColorGradingFilter &&
            config.ColorGradingContrast == shaders.ColorGradingContrast &&
            config.ColorGradingSaturation == shaders.ColorGradingSaturation &&
            config.ColorGradingToneMapping == shaders.ColorGradingToneMapping &&
            config.ColorGradingToneMappingWhitePoint == shaders.ColorGradingToneMappingWhitePoint &&
            config.EigengrauIntensity == shaders.EigengrauIntensity &&
            config.EigengrauColor == shaders.EigengrauColor &&
            config.EigengrauDarknessThreshold == shaders.EigengrauDarknessThreshold &&
            config.EigengrauNoiseScale == shaders.EigengrauNoiseScale &&
            config.EigengrauAnimationSpeed == shaders.EigengrauAnimationSpeed &&
            config.MotionBlurIntensity == shaders.MotionBlurIntensity;
    }

    private static void ValidateFloat(float value, float minimum, float maximum, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                $"Client config value '{name}' must be finite and within [{minimum}, {maximum}].");
        }
    }

    private static void ValidateInt(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                $"Client config value '{name}' must be within [{minimum}, {maximum}].");
        }
    }

    private static void ValidateColor(Color value, string name)
    {
        ValidateFloat(value.r, 0f, float.MaxValue, $"{name}.r");
        ValidateFloat(value.g, 0f, float.MaxValue, $"{name}.g");
        ValidateFloat(value.b, 0f, float.MaxValue, $"{name}.b");
        ValidateFloat(value.a, 0f, float.MaxValue, $"{name}.a");
    }
}
