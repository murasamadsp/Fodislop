#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Eigengrau")]
    public class EigengrauComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Strength of fine animated film grain in near-black areas. This is the effect's only strength control.")]
        public ClampedFloatParameter intensity = new(0.2f, 0f, 1f);

        [Tooltip("Subtle tint of the grain in near-black areas.")]
        public ColorParameter color = new(new Color(0.018f, 0.02f, 0.028f, 1f));

        [Tooltip("Maximum perceptual (sRGB) luminance affected by Eigengrau. Fully lit pixels are excluded.")]
        public ClampedFloatParameter darknessThreshold = new(0.18f, 0.02f, 0.75f);

        [Tooltip("Film-grain size in physical screen pixels.")]
        public ClampedFloatParameter noiseScale = new(1f, 0.75f, 2f);

        [Tooltip("How many independent grain patterns are generated per second.")]
        public ClampedFloatParameter animationSpeed = new(18f, 1f, 60f);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
