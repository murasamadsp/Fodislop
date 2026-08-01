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
        public ClampedFloatParameter intensity = new(0f, 0f, 5f);

        [Tooltip("Minimum source brightness that contributes to Bloom.")]
        public ClampedFloatParameter threshold = new(0.9f, 0f, 2f);

        [Tooltip("How widely the glow spreads. It does not change brightness directly.")]
        public ClampedFloatParameter scatter = new(0.7f, 0.1f, 1f);

        [Tooltip("Color multiplier applied to the glow.")]
        public ColorParameter tint = new(Color.white);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
