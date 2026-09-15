#nullable enable

using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Core;

[Serializable]
internal sealed class ClientConfigLegacySchema21
{
    public int SchemaVersion;
    public bool DiffuseBounceEnabled;
    public float AmbientIntensity;
    public float EmissionScale;
    public Color AmbientColor;
    [FormerlySerializedAs("EmptyExtinctionRgb")]
    public Color EmptyExtinctionRGB;

    [FormerlySerializedAs("SolidExtinctionRgb")]
    public Color SolidExtinctionRGB;
    public float EmptyExtinctionMultiplier;
    public float SolidExtinctionMultiplier;
    public float BounceStrength;
    public float MaximumLightMultiplier;
    public float TransmittanceDebugDistanceCells;
    public float MinimumTransmission;
    public int LightSafeBorder;
    public float DynamicLightIntensity;
    public Color DynamicLightColor;
    public float DynamicLightUpdatesPerSecond;

    public Vector2 TerrainFlowScale;
    public float TerrainShimmerSpeedScale;
    public float TerrainPulseSpeedScale;
    public Color TerrainShimmerColor;
    public Color TerrainDebugColor;
    public bool TerrainDebugMode;
    public bool EnableTerrainDistortion;
    public Color TransitEmissionColor;
    public float TransitEmissionStrength;
    public Color PerspectiveEmissionColor;
    public float PerspectiveEmissionStrength;
    public float SurfaceOccupancy;

    public bool BloomEnabled;
    public bool VignetteEnabled;
    public bool FilmGrainEnabled;
    public bool MotionBlurEnabled;

    public WorldLightingSettings ToLighting() => new();

    public TerrainSettings ToTerrain() => new()
    {
        FlowScale = TerrainFlowScale,
        ShimmerSpeedScale = TerrainShimmerSpeedScale,
        PulseSpeedScale = TerrainPulseSpeedScale,
        ShimmerColor = TerrainShimmerColor,
        DebugColor = TerrainDebugColor,
        DebugMode = TerrainDebugMode,
        EnableDistortion = EnableTerrainDistortion,
        TransitEmissionColor = TransitEmissionColor,
        TransitEmissionStrength = TransitEmissionStrength,
        PerspectiveEmissionColor = PerspectiveEmissionColor,
        PerspectiveEmissionStrength = PerspectiveEmissionStrength,
        SurfaceOccupancy = SurfaceOccupancy,
    };

    public EffectSettings ToEffects() => new()
    {
        BloomEnabled = BloomEnabled,
        VignetteEnabled = VignetteEnabled,
        EigengrauEnabled = FilmGrainEnabled,
        MotionBlurEnabled = MotionBlurEnabled,
    };
}
