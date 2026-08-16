#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core;

public sealed class ProjectDefaultsSnapshot : IProjectDefaults
{
    public ProjectDefaultsSnapshot(
        int schemaVersion,
        string contentHash,
        ClientDefaultsSnapshot client,
        LightingDefaultsSnapshot lighting,
        ShaderDefaultsSnapshot shaders)
    {
        SchemaVersion = schemaVersion;
        ContentHash = contentHash;
        Client = client;
        Lighting = lighting;
        Shaders = shaders;
    }

    public int SchemaVersion { get; }

    public string ContentHash { get; }

    public ClientDefaultsSnapshot Client { get; }

    public LightingDefaultsSnapshot Lighting { get; }

    public ShaderDefaultsSnapshot Shaders { get; }
}

public readonly record struct ClientDefaultsSnapshot(
    float MasterVolume,
    float SfxVolume,
    float MusicVolume,
    float AmbienceVolume,
    float VoiceVolume,
    float UiVolume,
    float UiScale,
    int GraphicsQuality);

public readonly record struct LightingDefaultsSnapshot(
    bool AmbientOcclusionEnabled,
    bool DiffuseBounceEnabled,
    float AmbientIntensity,
    float EmissionScale,
    Color AmbientColor,
    Color EmptyExtinctionRgb,
    Color SolidExtinctionRgb,
    float EmptyExtinctionMultiplier,
    float SolidExtinctionMultiplier,
    float BounceStrength,
    float AmbientOcclusionRadiusCells,
    float AmbientOcclusionStrength,
    float MaximumLightMultiplier,
    bool EnableFinalLightingClamp,
    float TransmittanceDebugDistanceCells,
    float MinimumTransmission,
    int LightSafeBorder,
    float DynamicLightIntensity,
    Color DynamicLightColor,
    float DynamicLightUpdatesPerSecond);

public readonly record struct ShaderDefaultsSnapshot(
    Vector2 TerrainFlowScale,
    float TerrainShimmerSpeedScale,
    float TerrainPulseSpeedScale,
    Color TerrainShimmerColor,
    Color TerrainDebugColor,
    bool TerrainDebugMode,
    float BloomThreshold,
    float BloomScatter,
    Color BloomTint,
    Color TransitEmissionColor,
    float TransitEmissionStrength,
    Color PerspectiveEmissionColor,
    float PerspectiveEmissionStrength,
    float SurfaceOccupancy,
    float BloomIntensity,
    float VignetteIntensity,
    Color VignetteColor,
    float VignetteSmoothness,
    Vector2 VignetteCenter,
    float ChromaticAberrationIntensity,
    float ColorGradingExposure,
    Color ColorGradingFilter,
    float ColorGradingContrast,
    float ColorGradingSaturation,
    bool ColorGradingToneMapping,
    float ColorGradingToneMappingWhitePoint,
    float EigengrauIntensity,
    Color EigengrauColor,
    float EigengrauDarknessThreshold,
    float EigengrauNoiseScale,
    float EigengrauAnimationSpeed,
    float MotionBlurIntensity,
    int MotionBlurMaxSamples);
