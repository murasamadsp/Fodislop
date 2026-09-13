#nullable enable

namespace Fodinae.Rendering.PostProcessing;

public readonly record struct AdvancedPostProcessSnapshot(
    float LocalContrastIntensity,
    float LensDirtIntensity,
    float LensDirtScale,
    float AnamorphicIntensity,
    float AnamorphicLength,
    float ChromaticDiffractionIntensity,
    float HeatRefractionIntensity,
    float HeatRefractionScale,
    float GlintIntensity,
    float GlintThreshold,
    float VolumetricDustIntensity,
    float VolumetricDustScale,
    float VolumetricDustSpeed,
    float PhosphorMaskIntensity,
    float DitheringIntensity,
    float TemporalPersistenceIntensity,
    float TemporalPersistenceDecay,
    float LightStability)
{
    public bool RequiresBloomTexture =>
        LensDirtIntensity > 0f ||
        AnamorphicIntensity > 0f ||
        ChromaticDiffractionIntensity > 0f;
}
