#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Motion Blur")]
    public class MotionBlurComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Directional blur strength for remote robots. The local player and UI are excluded.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.MotionBlurIntensity();

        [Tooltip("Samples along each remote robot's per-frame motion vector.")]
        public ClampedIntParameter maxSamples = PostProcessDefaults.MotionBlurMaxSamples();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
