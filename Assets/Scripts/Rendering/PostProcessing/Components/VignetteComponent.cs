#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Vignette")]
    public class VignetteComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Opacity of the edge darkening. Zero disables the effect.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.VignetteIntensity();

        [Tooltip("Color applied at the screen edges.")]
        public ColorParameter color = PostProcessDefaults.VignetteColor();

        [Tooltip("Width of the feathered transition between center and edges.")]
        public ClampedFloatParameter smoothness = PostProcessDefaults.VignetteSmoothness();

        [Tooltip("Normalized center of the vignette.")]
        public Vector2Parameter center = PostProcessDefaults.VignetteCenter();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
