#nullable enable

using System;
using UnityEngine;

namespace Fodinae.World.Lighting;

[Serializable]
public sealed class LightingRuntimeConfig
{
    public const string SchemaId = "Fodinae.Lighting.RuntimeConfig";
    public const int CurrentVersion = 1;

    public string Schema = string.Empty;
    public int Version = CurrentVersion;
    public bool AmbientOcclusionEnabled;
    public bool DiffuseBounceEnabled;
    public float AmbientIntensity;
    public float EmissionScale;
    public Color AmbientColor;
    public Color EmptyExtinctionRgb;
    public Color SolidExtinctionRgb;
    public float EmptyExtinctionMultiplier;
    public float SolidExtinctionMultiplier;
    public float BounceStrength;
    public float AmbientOcclusionRadiusCells;
    public float AmbientOcclusionStrength;
    public float MaximumLightMultiplier;
    public bool EnableFinalLightingClamp;
    public float TransmittanceDebugDistanceCells;
    public float MinimumTransmission;
    public int LightSafeBorder;
    public float DynamicLightIntensity;
    public Color DynamicLightColor;
    public float DynamicLightUpdatesPerSecond;

    public void Validate()
    {
        if (Schema != SchemaId)
        {
            throw new InvalidOperationException(
                "Lighting config schema is missing or unsupported.");
        }

        if (Version != CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported lighting config version {Version}; expected {CurrentVersion}.");
        }

        ValidateFinite(AmbientIntensity, nameof(AmbientIntensity));
        ValidateFinite(EmissionScale, nameof(EmissionScale));
        ValidateFinite(EmptyExtinctionMultiplier, nameof(EmptyExtinctionMultiplier));
        ValidateFinite(SolidExtinctionMultiplier, nameof(SolidExtinctionMultiplier));
        ValidateFinite(BounceStrength, nameof(BounceStrength));
        ValidateFinite(AmbientOcclusionRadiusCells, nameof(AmbientOcclusionRadiusCells));
        ValidateFinite(AmbientOcclusionStrength, nameof(AmbientOcclusionStrength));
        ValidateFinite(MaximumLightMultiplier, nameof(MaximumLightMultiplier));
        ValidateFinite(TransmittanceDebugDistanceCells, nameof(TransmittanceDebugDistanceCells));
        ValidateFinite(MinimumTransmission, nameof(MinimumTransmission));
        ValidateFinite(DynamicLightIntensity, nameof(DynamicLightIntensity));
        ValidateFinite(DynamicLightUpdatesPerSecond, nameof(DynamicLightUpdatesPerSecond));
        ValidateColor(AmbientColor, nameof(AmbientColor));
        ValidateColor(EmptyExtinctionRgb, nameof(EmptyExtinctionRgb));
        ValidateColor(SolidExtinctionRgb, nameof(SolidExtinctionRgb));
        ValidateColor(DynamicLightColor, nameof(DynamicLightColor));

        ValidateRange(AmbientIntensity, 0f, 1f, nameof(AmbientIntensity));
        ValidateRange(EmissionScale, 0.1f, 8f, nameof(EmissionScale));
        ValidateRange(EmptyExtinctionMultiplier, 0f, 2f, nameof(EmptyExtinctionMultiplier));
        ValidateRange(SolidExtinctionMultiplier, 0.25f, 2f, nameof(SolidExtinctionMultiplier));
        ValidateRange(BounceStrength, 0f, 1f, nameof(BounceStrength));
        ValidateRange(AmbientOcclusionRadiusCells, 0.5f, 8f, nameof(AmbientOcclusionRadiusCells));
        ValidateRange(AmbientOcclusionStrength, 0.1f, 8f, nameof(AmbientOcclusionStrength));
        ValidateRange(MaximumLightMultiplier, 0.25f, LightingConfigLimits.MaximumLightMultiplier, nameof(MaximumLightMultiplier));
        ValidateRange(TransmittanceDebugDistanceCells, 2f, 32f, nameof(TransmittanceDebugDistanceCells));
        ValidateRange(MinimumTransmission, 0.0001f, 0.1f, nameof(MinimumTransmission));
        if (LightSafeBorder is < 0 or > 8)
        {
            throw new InvalidOperationException($"Lighting config value 'LightSafeBorder' ({LightSafeBorder}) is out of range [0, 8].");
        }

        ValidateRange(DynamicLightIntensity, 0f, 4f, nameof(DynamicLightIntensity));
        ValidateRange(DynamicLightUpdatesPerSecond, 1f, LightingConfigLimits.DynamicLightUpdatesPerSecond, nameof(DynamicLightUpdatesPerSecond));
    }

    private static void ValidateRange(float value, float min, float max, string propertyName)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"Lighting config value '{propertyName}' ({value}) is out of range [{min}, {max}].");
        }
    }

    private static void ValidateFinite(float value, string propertyName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new InvalidOperationException($"Lighting config value '{propertyName}' is not finite.");
        }
    }

    private static void ValidateColor(Color value, string propertyName)
    {
        ValidateFinite(value.r, $"{propertyName}.r");
        ValidateFinite(value.g, $"{propertyName}.g");
        ValidateFinite(value.b, $"{propertyName}.b");
        ValidateFinite(value.a, $"{propertyName}.a");
        if (value.r < 0f || value.g < 0f || value.b < 0f || value.a < 0f)
        {
            throw new InvalidOperationException($"Lighting config color '{propertyName}' contains a negative channel.");
        }
    }
}
