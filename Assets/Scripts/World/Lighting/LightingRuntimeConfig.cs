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
    public int Quality = (int)LightingDefaults.Quality;
    public bool AmbientOcclusionEnabled = LightingDefaults.AmbientOcclusionEnabled;
    public bool DiffuseBounceEnabled = LightingDefaults.DiffuseBounceEnabled;
    public float AmbientIntensity = LightingDefaults.AmbientIntensity;
    public float EmissionScale = LightingDefaults.EmissionScale;
    public Color AmbientColor = LightingDefaults.AmbientColor;
    public Color EmptyExtinctionRgb = LightingDefaults.EmptyExtinctionRgb;
    public Color SolidExtinctionRgb = LightingDefaults.SolidExtinctionRgb;
    public float EmptyExtinctionMultiplier = LightingDefaults.EmptyExtinctionMultiplier;
    public float SolidExtinctionMultiplier = LightingDefaults.SolidExtinctionMultiplier;
    public float BounceStrength = LightingDefaults.BounceStrength;
    public float AmbientOcclusionRadiusCells = LightingDefaults.AmbientOcclusionRadiusCells;
    public float AmbientOcclusionStrength = LightingDefaults.AmbientOcclusionStrength;
    public float MaximumLightMultiplier = LightingDefaults.MaximumLightMultiplier;
    public bool EnableFinalLightingClamp = LightingDefaults.EnableFinalLightingClamp;
    public float TransmittanceDebugDistanceCells = LightingDefaults.TransmittanceDebugDistanceCells;
    public float MinimumTransmission = LightingDefaults.MinimumTransmission;
    public int LightSafeBorder = LightingDefaults.LightSafeBorder;
    public float DynamicLightIntensity = LightingDefaults.DynamicLightIntensity;
    public Color DynamicLightColor = LightingDefaults.DynamicLightColor;
    public float DynamicLightUpdatesPerSecond = LightingDefaults.DynamicLightUpdatesPerSecond;

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

        if (Quality < (int)TerrariaLightingEngine.QualityPreset.Low ||
            Quality > (int)TerrariaLightingEngine.QualityPreset.Ultra ||
            AmbientIntensity is < 0f or > 1f ||
            EmissionScale is < 0.1f or > 8f ||
            EmptyExtinctionMultiplier is < 0f or > 2f ||
            SolidExtinctionMultiplier is < 0.25f or > 2f ||
            BounceStrength is < 0f or > 1f ||
            AmbientOcclusionRadiusCells is < 0.5f or > 8f ||
            AmbientOcclusionStrength is < 0.1f or > 8f ||
            MaximumLightMultiplier is < 0.25f or > LightingDefaults.MaximumLightMultiplierLimit ||
            TransmittanceDebugDistanceCells is < 2f or > 32f ||
            MinimumTransmission is < 0.0001f or > 0.1f ||
            LightSafeBorder is < 0 or > 8 ||
            DynamicLightIntensity is < 0f or > 4f ||
            DynamicLightUpdatesPerSecond is < 1f or > LightingDefaults.DynamicLightUpdatesPerSecondLimit)
        {
            throw new InvalidOperationException("Lighting runtime config contains an out-of-range value.");
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
