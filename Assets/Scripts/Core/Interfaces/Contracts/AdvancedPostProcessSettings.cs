#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

[Serializable]
public sealed class AdvancedPostProcessSettings
{
    public float LocalContrastIntensity;
    public float LensDirtIntensity;
    public float LensDirtScale = 3f;
    public float AnamorphicIntensity;
    public float AnamorphicLength = 1f;
    public float ChromaticDiffractionIntensity;
    public float HeatRefractionIntensity;
    public float HeatRefractionScale = 2f;
    public float GlintIntensity;
    public float GlintThreshold = 1f;
    public float VolumetricDustIntensity;
    public float VolumetricDustScale = 1f;
    public float VolumetricDustSpeed = 0.1f;
    public float PhosphorMaskIntensity;
    public float DitheringIntensity;
    public float TemporalPersistenceIntensity;
    public float TemporalPersistenceDecay = 0.85f;
    public float LightStability;

    public bool HasCurrentFrameEffects()
    {
        return LocalContrastIntensity > 0f ||
            LensDirtIntensity > 0f ||
            AnamorphicIntensity > 0f ||
            ChromaticDiffractionIntensity > 0f ||
            HeatRefractionIntensity > 0f ||
            GlintIntensity > 0f ||
            VolumetricDustIntensity > 0f ||
            PhosphorMaskIntensity > 0f ||
            DitheringIntensity > 0f;
    }

    public bool HasAnyEffects()
    {
        return HasCurrentFrameEffects() ||
            TemporalPersistenceIntensity > 0f ||
            LightStability > 0f;
    }

    public bool RequiresBloomTexture()
    {
        return LensDirtIntensity > 0f ||
            AnamorphicIntensity > 0f ||
            ChromaticDiffractionIntensity > 0f;
    }
}

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
    public bool HasCurrentFrameEffects =>
        LocalContrastIntensity > 0f ||
        LensDirtIntensity > 0f ||
        AnamorphicIntensity > 0f ||
        ChromaticDiffractionIntensity > 0f ||
        HeatRefractionIntensity > 0f ||
        GlintIntensity > 0f ||
        VolumetricDustIntensity > 0f ||
        PhosphorMaskIntensity > 0f ||
        DitheringIntensity > 0f;

    public bool HasAnyEffects =>
        HasCurrentFrameEffects ||
        TemporalPersistenceIntensity > 0f ||
        LightStability > 0f;

    public bool RequiresBloomTexture =>
        LensDirtIntensity > 0f ||
        AnamorphicIntensity > 0f ||
        ChromaticDiffractionIntensity > 0f;

    public static AdvancedPostProcessSnapshot From(AdvancedPostProcessSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return new AdvancedPostProcessSnapshot(
            settings.LocalContrastIntensity,
            settings.LensDirtIntensity,
            settings.LensDirtScale,
            settings.AnamorphicIntensity,
            settings.AnamorphicLength,
            settings.ChromaticDiffractionIntensity,
            settings.HeatRefractionIntensity,
            settings.HeatRefractionScale,
            settings.GlintIntensity,
            settings.GlintThreshold,
            settings.VolumetricDustIntensity,
            settings.VolumetricDustScale,
            settings.VolumetricDustSpeed,
            settings.PhosphorMaskIntensity,
            settings.DitheringIntensity,
            settings.TemporalPersistenceIntensity,
            settings.TemporalPersistenceDecay,
            settings.LightStability);
    }
}
