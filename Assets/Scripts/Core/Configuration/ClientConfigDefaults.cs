#nullable enable

using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;

namespace Fodinae.Core;

internal static class ClientConfigDefaults
{
    public static ClientConfig Create(
        IProjectDefaults projectDefaults,
        GraphicsQualityProfile graphicsQualityProfile)
    {
        if (projectDefaults == null)
        {
            throw new System.ArgumentNullException(nameof(projectDefaults));
        }

        if (graphicsQualityProfile == null)
        {
            throw new System.ArgumentNullException(nameof(graphicsQualityProfile));
        }

        ClientDefaultsSnapshot defaults = projectDefaults.Client;
        LightingDefaultsSnapshot lighting = projectDefaults.Lighting;
        ShaderDefaultsSnapshot shaders = projectDefaults.Shaders;
        GraphicsPreset graphicsPreset = ConvertLegacyGraphicsQuality(
            defaults.GraphicsQuality);
        return new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = projectDefaults.ContentHash,
            MasterVolume = defaults.MasterVolume,
            SfxVolume = defaults.SfxVolume,
            MusicVolume = defaults.MusicVolume,
            AmbienceVolume = defaults.AmbienceVolume,
            VoiceVolume = defaults.VoiceVolume,
            UIVolume = defaults.UIVolume,
            UIScale = defaults.UIScale,
            GraphicsPreset = graphicsPreset,
            GraphicsQualitySettings = graphicsQualityProfile.Get(graphicsPreset),
            AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled,
            DiffuseBounceEnabled = lighting.DiffuseBounceEnabled,
            AmbientIntensity = lighting.AmbientIntensity,
            EmissionScale = lighting.EmissionScale,
            AmbientColor = lighting.AmbientColor,
            EmptyExtinctionRgb = lighting.EmptyExtinctionRgb,
            SolidExtinctionRgb = lighting.SolidExtinctionRgb,
            EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier,
            SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier,
            BounceStrength = lighting.BounceStrength,
            AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells,
            AmbientOcclusionStrength = lighting.AmbientOcclusionStrength,
            MaximumLightMultiplier = lighting.MaximumLightMultiplier,
            EnableFinalLightingClamp = lighting.EnableFinalLightingClamp,
            TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells,
            MinimumTransmission = lighting.MinimumTransmission,
            LightSafeBorder = lighting.LightSafeBorder,
            DynamicLightIntensity = lighting.DynamicLightIntensity,
            DynamicLightColor = lighting.DynamicLightColor,
            DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond,
            TerrainFlowScale = shaders.TerrainFlowScale,
            TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale,
            TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale,
            TerrainShimmerColor = shaders.TerrainShimmerColor,
            TerrainDebugColor = shaders.TerrainDebugColor,
            TerrainDebugMode = shaders.TerrainDebugMode,
            BloomThreshold = shaders.BloomThreshold,
            BloomSoftKnee = shaders.BloomSoftKnee,
            BloomRadius = shaders.BloomRadius,
            BloomScatter = shaders.BloomScatter,
            BloomTint = shaders.BloomTint,
            TransitEmissionColor = shaders.TransitEmissionColor,
            TransitEmissionStrength = shaders.TransitEmissionStrength,
            PerspectiveEmissionColor = shaders.PerspectiveEmissionColor,
            PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength,
            SurfaceOccupancy = shaders.SurfaceOccupancy,
            BloomIntensity = shaders.BloomIntensity,
            VignetteIntensity = shaders.VignetteIntensity,
            VignetteColor = shaders.VignetteColor,
            VignetteSmoothness = shaders.VignetteSmoothness,
            VignetteCenter = shaders.VignetteCenter,
            ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity,
            ColorGradingExposure = shaders.ColorGradingExposure,
            ColorGradingFilter = shaders.ColorGradingFilter,
            ColorGradingContrast = shaders.ColorGradingContrast,
            ColorGradingSaturation = shaders.ColorGradingSaturation,
            ColorGradingToneMapping = shaders.ColorGradingToneMapping,
            ColorGradingToneMappingWhitePoint = shaders.ColorGradingToneMappingWhitePoint,
            EigengrauIntensity = shaders.EigengrauIntensity,
            EigengrauColor = shaders.EigengrauColor,
            EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold,
            EigengrauNoiseScale = shaders.EigengrauNoiseScale,
            EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed,
            MotionBlurIntensity = shaders.MotionBlurIntensity,
            UseDummyConnection = true,
            ServerHost = "127.0.0.1",
            ServerPort = 7777,
        };
    }

    public static GraphicsPreset ConvertLegacyGraphicsQuality(int legacyQuality)
    {
        return legacyQuality switch
        {
            0 => GraphicsPreset.Low,
            1 => GraphicsPreset.Medium,
            2 => GraphicsPreset.High,
            3 => GraphicsPreset.Ultra,
            _ => throw new InvalidDataException(
                $"Legacy graphics quality '{legacyQuality}' is outside the supported range 0..3."),
        };
    }

    public static void ApplyShaderDefaults(
        ClientConfig config,
        ShaderDefaultsSnapshot shaders)
    {
        config.TerrainFlowScale = shaders.TerrainFlowScale;
        config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
        config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
        config.TerrainShimmerColor = shaders.TerrainShimmerColor;
        config.TerrainDebugColor = shaders.TerrainDebugColor;
        config.TerrainDebugMode = shaders.TerrainDebugMode;
        config.BloomThreshold = shaders.BloomThreshold;
        config.BloomSoftKnee = shaders.BloomSoftKnee;
        config.BloomRadius = shaders.BloomRadius;
        config.BloomScatter = shaders.BloomScatter;
        config.BloomTint = shaders.BloomTint;
        config.TransitEmissionColor = shaders.TransitEmissionColor;
        config.TransitEmissionStrength = shaders.TransitEmissionStrength;
        config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
        config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
        config.SurfaceOccupancy = shaders.SurfaceOccupancy;
        config.BloomIntensity = shaders.BloomIntensity;
        config.VignetteIntensity = shaders.VignetteIntensity;
        config.VignetteColor = shaders.VignetteColor;
        config.VignetteSmoothness = shaders.VignetteSmoothness;
        config.VignetteCenter = shaders.VignetteCenter;
        config.ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity;
        config.ColorGradingExposure = shaders.ColorGradingExposure;
        config.ColorGradingFilter = shaders.ColorGradingFilter;
        config.ColorGradingContrast = shaders.ColorGradingContrast;
        config.ColorGradingSaturation = shaders.ColorGradingSaturation;
        config.ColorGradingToneMapping = shaders.ColorGradingToneMapping;
        config.ColorGradingToneMappingWhitePoint = shaders.ColorGradingToneMappingWhitePoint;
        config.EigengrauIntensity = shaders.EigengrauIntensity;
        config.EigengrauColor = shaders.EigengrauColor;
        config.EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold;
        config.EigengrauNoiseScale = shaders.EigengrauNoiseScale;
        config.EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed;
        config.MotionBlurIntensity = shaders.MotionBlurIntensity;
    }

    public static void ApplyLightingDefaults(
        ClientConfig config,
        LightingDefaultsSnapshot lighting)
    {
        config.AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled;
        config.DiffuseBounceEnabled = lighting.DiffuseBounceEnabled;
        config.AmbientIntensity = lighting.AmbientIntensity;
        config.EmissionScale = lighting.EmissionScale;
        config.AmbientColor = lighting.AmbientColor;
        config.EmptyExtinctionRgb = lighting.EmptyExtinctionRgb;
        config.SolidExtinctionRgb = lighting.SolidExtinctionRgb;
        config.EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier;
        config.SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier;
        config.BounceStrength = lighting.BounceStrength;
        config.AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells;
        config.AmbientOcclusionStrength = lighting.AmbientOcclusionStrength;
        config.MaximumLightMultiplier = lighting.MaximumLightMultiplier;
        config.EnableFinalLightingClamp = lighting.EnableFinalLightingClamp;
        config.TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells;
        config.MinimumTransmission = lighting.MinimumTransmission;
        config.LightSafeBorder = lighting.LightSafeBorder;
        config.DynamicLightIntensity = lighting.DynamicLightIntensity;
        config.DynamicLightColor = lighting.DynamicLightColor;
        config.DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond;
    }
}
