#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;

namespace Fodinae.Core;

internal sealed class ClientConfigMigration(
    IProjectDefaults projectDefaults,
    GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly IProjectDefaults _projectDefaults = projectDefaults ??
        throw new ArgumentNullException(nameof(projectDefaults));
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    public bool Migrate(ClientConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
        bool migrated = false;
        if (config.SchemaVersion < 2)
        {
            ApplySchema2(config, shaders);
            migrated = true;
        }

        if (config.SchemaVersion < 3)
        {
            ApplySchema3(config, shaders);
            migrated = true;
        }

        if (config.SchemaVersion < 4)
        {
            config.TerrainDebugColor = shaders.TerrainDebugColor;
            config.TerrainDebugMode = shaders.TerrainDebugMode;
            config.SchemaVersion = 4;
            migrated = true;
        }

        if (config.SchemaVersion < 5)
        {
            config.SchemaVersion = 5;
            migrated = true;
        }

        if (config.SchemaVersion < 6)
        {
            ClientConfigDefaults.ApplyShaderDefaults(config, shaders);
            config.ProjectDefaultsHash = _projectDefaults.ContentHash;
            config.SchemaVersion = 6;
            migrated = true;
        }

        if (config.SchemaVersion < 7)
        {
            config.ProjectDefaultsHash = _projectDefaults.ContentHash;
            config.SchemaVersion = 7;
            migrated = true;
        }

        if (config.SchemaVersion < 8)
        {
            ClientConfigDefaults.ApplyLightingDefaults(config, _projectDefaults.Lighting);
            config.SchemaVersion = 8;
            migrated = true;
        }

        if (config.SchemaVersion < 9)
        {
            GraphicsPreset previousPreset = ClientConfigDefaults.ConvertLegacyGraphicsQuality(
                (int)config.GraphicsPreset);
            config.GraphicsQualitySettings = _graphicsQualityProfile.Get(previousPreset);
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.SchemaVersion = 9;
            migrated = true;
        }

        if (config.SchemaVersion < 10)
        {
            config.UseDummyConnection = true;
            config.ServerHost = "127.0.0.1";
            config.ServerPort = 7777;
            config.SchemaVersion = 10;
            migrated = true;
        }

        if (config.SchemaVersion < 11)
        {
            config.SchemaVersion = 11;
            migrated = true;
        }

        if (config.SchemaVersion < 12)
        {
            config.GraphicsQualitySettings.LightingMaximumTextureDimension =
                Mathf.Max(
                    config.GraphicsQualitySettings.LightingMaximumTextureDimension,
                    GraphicsQualitySettings.MinimumLightingTextureDimension);
            config.SchemaVersion = 12;
            migrated = true;
        }

        if (config.SchemaVersion < 13)
        {
            config.BloomSoftKnee = shaders.BloomSoftKnee;
            config.BloomRadius = shaders.BloomRadius;
            config.SchemaVersion = 13;
            migrated = true;
        }

        if (config.SchemaVersion < 14)
        {
            config.AdvancedPostProcess = new AdvancedPostProcessSettings();
            config.SchemaVersion = 14;
            migrated = true;
        }

        if (config.SchemaVersion < 15)
        {
            ApplySchema15(config);
            migrated = true;
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            GraphicsQualitySettings standardSettings =
                _graphicsQualityProfile.Get(config.GraphicsPreset);
            if (config.GraphicsQualitySettings != standardSettings)
            {
                config.GraphicsQualitySettings = standardSettings;
                migrated = true;
            }
        }

        if (!string.Equals(
                config.ProjectDefaultsHash,
                _projectDefaults.ContentHash,
                StringComparison.Ordinal))
        {
            RefreshChangedProjectDefaults(config);
            migrated = true;
        }

        return migrated;
    }

    private static void ApplySchema2(ClientConfig config, ShaderDefaultsSnapshot shaders)
    {
        config.TerrainFlowScale = shaders.TerrainFlowScale;
        config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
        config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
        config.TerrainShimmerColor = shaders.TerrainShimmerColor;
        config.TerrainDebugColor = shaders.TerrainDebugColor;
        config.TerrainDebugMode = shaders.TerrainDebugMode;
        config.TransitEmissionColor = shaders.TransitEmissionColor;
        config.TransitEmissionStrength = shaders.TransitEmissionStrength;
        config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
        config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
        config.SurfaceOccupancy = shaders.SurfaceOccupancy;
        config.BloomIntensity = shaders.BloomIntensity;
        config.VignetteIntensity = shaders.VignetteIntensity;
        config.ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity;
        config.ColorGradingExposure = shaders.ColorGradingExposure;
        config.ColorGradingContrast = shaders.ColorGradingContrast;
        config.ColorGradingSaturation = shaders.ColorGradingSaturation;
        config.ColorGradingToneMapping = shaders.ColorGradingToneMapping;
        config.EigengrauIntensity = shaders.EigengrauIntensity;
        config.MotionBlurIntensity = shaders.MotionBlurIntensity;
        config.SchemaVersion = 2;
    }

    private static void ApplySchema3(ClientConfig config, ShaderDefaultsSnapshot shaders)
    {
        config.BloomThreshold = shaders.BloomThreshold;
        config.BloomScatter = shaders.BloomScatter;
        config.BloomTint = shaders.BloomTint;
        config.VignetteColor = shaders.VignetteColor;
        config.VignetteSmoothness = shaders.VignetteSmoothness;
        config.VignetteCenter = shaders.VignetteCenter;
        config.ColorGradingFilter = shaders.ColorGradingFilter;
        config.ColorGradingToneMappingWhitePoint = shaders.ColorGradingToneMappingWhitePoint;
        config.EigengrauColor = shaders.EigengrauColor;
        config.EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold;
        config.EigengrauNoiseScale = shaders.EigengrauNoiseScale;
        config.EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed;
        config.SchemaVersion = 3;
    }

    private static void ApplySchema15(ClientConfig config)
    {
        AdvancedPostProcessSettings advanced = config.AdvancedPostProcess;
        config.BloomIntensity = Mathf.Clamp(config.BloomIntensity, 0f, 2f);
        config.BloomTint = new Color(
            Mathf.Clamp(config.BloomTint.r, 0f, 2f),
            Mathf.Clamp(config.BloomTint.g, 0f, 2f),
            Mathf.Clamp(config.BloomTint.b, 0f, 2f),
            Mathf.Clamp01(config.BloomTint.a));
        config.ChromaticAberrationIntensity = Mathf.Clamp(config.ChromaticAberrationIntensity, 0f, 0.25f);
        config.ColorGradingExposure = Mathf.Clamp(config.ColorGradingExposure, -2f, 2f);
        config.ColorGradingContrast = Mathf.Clamp(config.ColorGradingContrast, -0.5f, 0.5f);
        config.EigengrauIntensity = Mathf.Clamp(config.EigengrauIntensity, 0f, 0.25f);
        config.MotionBlurIntensity = Mathf.Clamp(config.MotionBlurIntensity, 0f, 0.5f);
        advanced.LocalContrastIntensity = Mathf.Clamp(advanced.LocalContrastIntensity, 0f, 0.5f);
        advanced.LensDirtIntensity = Mathf.Clamp(advanced.LensDirtIntensity, 0f, 0.35f);
        advanced.AnamorphicIntensity = Mathf.Clamp01(advanced.AnamorphicIntensity);
        advanced.ChromaticDiffractionIntensity = Mathf.Clamp(advanced.ChromaticDiffractionIntensity, 0f, 0.5f);
        advanced.HeatRefractionIntensity = Mathf.Clamp(advanced.HeatRefractionIntensity, 0f, 0.25f);
        advanced.GlintIntensity = Mathf.Clamp(advanced.GlintIntensity, 0f, 0.5f);
        advanced.VolumetricDustIntensity = Mathf.Clamp(advanced.VolumetricDustIntensity, 0f, 0.25f);
        advanced.PhosphorMaskIntensity = Mathf.Clamp(advanced.PhosphorMaskIntensity, 0f, 0.35f);
        advanced.TemporalPersistenceIntensity = Mathf.Clamp(advanced.TemporalPersistenceIntensity, 0f, 0.8f);
        advanced.TemporalPersistenceDecay = Mathf.Clamp(advanced.TemporalPersistenceDecay, 0f, 0.98f);
        advanced.LightStability = Mathf.Clamp(advanced.LightStability, 0f, 0.9f);
        config.SchemaVersion = 15;
    }

    private void RefreshChangedProjectDefaults(ClientConfig config)
    {
        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            ClientConfigDefaults.ApplyLightingDefaults(config, _projectDefaults.Lighting);
            ClientConfigDefaults.ApplyShaderDefaults(config, _projectDefaults.Shaders);
            config.AdvancedPostProcess = new AdvancedPostProcessSettings();
            Debug.Log(
                "[ClientConfigMigration] ProjectDefaults changed; refreshed the selected " +
                "immutable standard graphics preset.");
        }
        else
        {
            Debug.Log(
                "[ClientConfigMigration] ProjectDefaults changed; preserved Custom graphics settings.");
        }

        config.ProjectDefaultsHash = _projectDefaults.ContentHash;
    }

}
