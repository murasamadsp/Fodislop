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
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Tooltip("Color applied at the screen edges.")]
        public ColorParameter color = new(Color.black);

        [Tooltip("Width of the feathered transition between center and edges.")]
        public ClampedFloatParameter smoothness = new(0.2f, 0.01f, 1f);

        [Tooltip("Normalized center of the vignette.")]
        public Vector2Parameter center = new(new Vector2(0.5f, 0.5f));

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
