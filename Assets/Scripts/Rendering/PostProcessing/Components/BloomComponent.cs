#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Bloom")]
    public class BloomComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Strength of the glow added around pixels brighter than Threshold.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.BloomIntensity();

        [Tooltip("Minimum source brightness that contributes to Bloom.")]
        public ClampedFloatParameter threshold = PostProcessDefaults.BloomThreshold();

        [Tooltip("How widely the glow spreads. It does not change brightness directly.")]
        public ClampedFloatParameter scatter = PostProcessDefaults.BloomScatter();

        [Tooltip("Color multiplier applied to the glow.")]
        public ColorParameter tint = PostProcessDefaults.BloomTint();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
