#nullable enable

using UnityEngine;

namespace Fodinae.World.Lighting;

public static class LightingDefaults
{
    public const TerrariaLightingEngine.QualityPreset Quality =
        TerrariaLightingEngine.QualityPreset.Ultra;
    public const float AmbientIntensity = 0.85f;
    public const float EmissionScale = 8f;
    public const float EmptyExtinctionMultiplier = 1f;
    public const float SolidExtinctionMultiplier = 2f;
    public const float BounceStrength = 1f;
    public const float AmbientOcclusionRadiusCells = 2f;
    public const float AmbientOcclusionStrength = 5f;
    public const float MaximumLightMultiplier = 1f;
    public const float MaximumLightMultiplierLimit = 16f;
    public const float TransmittanceDebugDistanceCells = 10f;
    public const float MinimumTransmission = 0.008f;
    public const int LightSafeBorder = 2;
    public const bool AmbientOcclusionEnabled = true;
    public const bool DiffuseBounceEnabled = true;
    public const bool EnableFinalLightingClamp = false;
    public const float DynamicLightIntensity = 1.25f;
    public const float DynamicLightUpdatesPerSecond = 20f;
    public const float DynamicLightUpdatesPerSecondLimit = 60f;

    public static Color AmbientColor => new(0.12f, 0.14f, 0.18f, 1f);

    public static Color EmptyExtinctionRgb => new(0.015f, 0.012f, 0.009f, 1f);

    public static Color SolidExtinctionRgb => new(1.2f, 1.1f, 1f, 1f);

    public static Color DynamicLightColor => Color.white;
}
