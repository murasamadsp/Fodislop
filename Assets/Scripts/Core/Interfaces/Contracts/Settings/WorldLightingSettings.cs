#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Освещение мира: константы для решателя радиансных каскадов.
/// Все значения фиксированы: ambient = белый, intensity = 1.
/// </summary>
[Serializable]
public sealed class WorldLightingSettings
{
    public const float AmbientIntensity = 1.0f;
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
