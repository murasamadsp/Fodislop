#nullable enable

using Fodinae.Core;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Quality;
using UnityEngine;

namespace Fodinae.World.Lighting;

internal static class LightingRuntimeConfigMapper
{
    public static LightingRuntimeConfig FromClientConfig(ClientConfig config)
    {
        LightingRuntimeConfig runtimeConfig = new()
        {
            Schema = LightingRuntimeConfig.SchemaId,
            Version = LightingRuntimeConfig.CurrentVersion,
            AmbientOcclusionEnabled = config.AmbientOcclusionEnabled,
            DiffuseBounceEnabled = config.DiffuseBounceEnabled,
            AmbientIntensity = Mathf.Clamp(config.AmbientIntensity, 0f, 1f),
            EmissionScale = Mathf.Clamp(config.EmissionScale <= 0f ? 1f : config.EmissionScale, 0.1f, 8f),
            AmbientColor = config.AmbientColor,
            EmptyExtinctionRgb = config.EmptyExtinctionRgb,
            SolidExtinctionRgb = config.SolidExtinctionRgb,
            EmptyExtinctionMultiplier = Mathf.Clamp(config.EmptyExtinctionMultiplier, 0f, 2f),
            SolidExtinctionMultiplier = Mathf.Clamp(
                config.SolidExtinctionMultiplier <= 0f ? 1f : config.SolidExtinctionMultiplier,
                0.25f,
                2f),
            BounceStrength = Mathf.Clamp(config.BounceStrength, 0f, 1f),
            AmbientOcclusionRadiusCells = Mathf.Clamp(
                config.AmbientOcclusionRadiusCells <= 0f ? 2f : config.AmbientOcclusionRadiusCells,
                0.5f,
                8f),
            AmbientOcclusionStrength = Mathf.Clamp(
                config.AmbientOcclusionStrength <= 0f ? 1f : config.AmbientOcclusionStrength,
                0.1f,
                8f),
            MaximumLightMultiplier = Mathf.Clamp(
                config.MaximumLightMultiplier <= 0f ? 1.5f : config.MaximumLightMultiplier,
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier),
            EnableFinalLightingClamp = config.EnableFinalLightingClamp,
            TransmittanceDebugDistanceCells = Mathf.Clamp(
                config.TransmittanceDebugDistanceCells <= 0f ? 16f : config.TransmittanceDebugDistanceCells,
                2f,
                32f),
            MinimumTransmission = Mathf.Clamp(
                config.MinimumTransmission <= 0f ? 0.01f : config.MinimumTransmission,
                0.0001f,
                0.1f),
            LightSafeBorder = Mathf.Clamp(config.LightSafeBorder, 0, 8),
            DynamicLightIntensity = Mathf.Clamp(config.DynamicLightIntensity, 0f, 4f),
            DynamicLightColor = config.DynamicLightColor,
            DynamicLightUpdatesPerSecond = Mathf.Clamp(
                config.DynamicLightUpdatesPerSecond <= 0f ? 30f : config.DynamicLightUpdatesPerSecond,
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond),
        };
        runtimeConfig.Validate();
        return runtimeConfig;
    }

    public static void ApplyToClientConfig(LightingRuntimeConfig source, ClientConfig target)
    {
        target.GraphicsPreset = GraphicsPreset.Custom;
        target.AmbientOcclusionEnabled = source.AmbientOcclusionEnabled;
        target.DiffuseBounceEnabled = source.DiffuseBounceEnabled;
        target.AmbientIntensity = source.AmbientIntensity;
        target.EmissionScale = source.EmissionScale;
        target.AmbientColor = source.AmbientColor;
        target.EmptyExtinctionRgb = source.EmptyExtinctionRgb;
        target.SolidExtinctionRgb = source.SolidExtinctionRgb;
        target.EmptyExtinctionMultiplier = source.EmptyExtinctionMultiplier;
        target.SolidExtinctionMultiplier = source.SolidExtinctionMultiplier;
        target.BounceStrength = source.BounceStrength;
        target.AmbientOcclusionRadiusCells = source.AmbientOcclusionRadiusCells;
        target.AmbientOcclusionStrength = source.AmbientOcclusionStrength;
        target.MaximumLightMultiplier = source.MaximumLightMultiplier;
        target.EnableFinalLightingClamp = source.EnableFinalLightingClamp;
        target.TransmittanceDebugDistanceCells = source.TransmittanceDebugDistanceCells;
        target.MinimumTransmission = source.MinimumTransmission;
        target.LightSafeBorder = source.LightSafeBorder;
        target.DynamicLightIntensity = source.DynamicLightIntensity;
        target.DynamicLightColor = source.DynamicLightColor;
        target.DynamicLightUpdatesPerSecond = source.DynamicLightUpdatesPerSecond;
    }
}
