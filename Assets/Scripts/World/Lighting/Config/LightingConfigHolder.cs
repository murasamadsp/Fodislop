#nullable enable

using UnityEngine;

namespace Fodinae.World.Lighting;

/// <summary>
/// Константы освещения. Все значения фиксированы: 1,1,1 (белый).
/// </summary>
internal static class LightingConfigHolder
{
    public const float AmbientIntensity = 0.3f;
    public const float EmissionScale = 1.0f;
    public static readonly Color AmbientColor = Color.white;
    public static readonly Color EmptyExtinctionRgb = Color.white;
    public static readonly Color SolidExtinctionRgb = Color.white;
    public const float EmptyExtinctionMultiplier = 1.0f;
    public const float SolidExtinctionMultiplier = 1.0f;
    public const float BounceStrength = 1.0f;
    public const float MaximumLightMultiplier = 1.0f;
    public const float MinimumTransmission = 0.008f;
    public const float DynamicLightIntensity = 1.0f;
    public static readonly Color DynamicLightColor = Color.white;
}
