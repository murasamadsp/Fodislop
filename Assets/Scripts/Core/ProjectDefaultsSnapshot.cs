#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core;

public sealed class ProjectDefaultsSnapshot : IProjectDefaults
{
    public ProjectDefaultsSnapshot(
        int schemaVersion,
        string contentHash,
        ClientDefaultsSnapshot client,
        LightingDefaultsSnapshot lighting)
    {
        SchemaVersion = schemaVersion;
        ContentHash = contentHash;
        Client = client;
        Lighting = lighting;
    }

    public int SchemaVersion { get; }

    public string ContentHash { get; }

    public ClientDefaultsSnapshot Client { get; }

    public LightingDefaultsSnapshot Lighting { get; }
}

public readonly record struct ClientDefaultsSnapshot(
    float MasterVolume,
    float SfxVolume,
    float MusicVolume,
    float AmbienceVolume,
    float VoiceVolume,
    float UiVolume,
    float UiScale,
    int GraphicsQuality,
    float RenderScale,
    int VSyncCount,
    int AntiAliasing);

public readonly record struct LightingDefaultsSnapshot(
    TerrariaLightingEngine.QualityPreset Quality,
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
