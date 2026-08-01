#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Chromatic Aberration")]
    public class ChromaticAberrationComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Radial RGB separation toward the screen edges. Zero disables the effect.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}
