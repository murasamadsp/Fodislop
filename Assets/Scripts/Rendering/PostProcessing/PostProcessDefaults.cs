#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Rendering.PostProcessing;

public static class PostProcessDefaults
{
    // These values only construct valid VolumeParameter instances for Unity
    // serialization. ProjectDefaults/ClientConfig is the sole visual source
    // of truth and overwrites every parameter before the first render.
    public static ClampedFloatParameter BloomIntensity() => new(0f, 0f, 5f);

    public static ClampedFloatParameter BloomThreshold() => new(0f, 0f, 2f);

    public static ClampedFloatParameter BloomSoftKnee() => new(0.5f, 0f, 1f);

    public static ClampedFloatParameter BloomRadius() => new(3f, 0.5f, 8f);

    public static ClampedFloatParameter BloomScatter() => new(0.1f, 0.1f, 1f);

    public static ColorParameter BloomTint() => new(Color.white);

    public static ClampedFloatParameter VignetteIntensity() => new(0f, 0f, 1f);

    public static ColorParameter VignetteColor() => new(Color.black);

    public static ClampedFloatParameter VignetteSmoothness() => new(0.01f, 0.01f, 1f);

    public static Vector2Parameter VignetteCenter() => new(new Vector2(0.5f, 0.5f));

    public static ClampedFloatParameter ChromaticAberrationIntensity() => new(0f, 0f, 1f);

    public static ClampedFloatParameter ColorGradingExposure() => new(0f, -4f, 4f);

    public static ColorParameter ColorGradingFilter() => new(Color.white);

    public static ClampedFloatParameter ColorGradingContrast() => new(0f, -1f, 1f);

    public static ClampedFloatParameter ColorGradingSaturation() => new(1f, 0f, 2f);

    public static BoolParameter ColorGradingToneMapping() => new(false);

    public static ClampedFloatParameter ColorGradingWhitePoint() => new(0.25f, 0.25f, 8f);

    public static ClampedFloatParameter EigengrauIntensity() => new(0f, 0f, 1f);

    public static ColorParameter EigengrauColor() => new(Color.black);

    public static ClampedFloatParameter EigengrauDarknessThreshold() => new(0.02f, 0.02f, 0.75f);

    public static ClampedFloatParameter EigengrauNoiseScale() => new(0.75f, 0.75f, 2f);

    public static ClampedFloatParameter EigengrauAnimationSpeed() => new(1f, 1f, 60f);

    public static ClampedFloatParameter MotionBlurIntensity() => new(0f, 0f, 1f);
}
