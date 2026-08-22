#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Fodinae/Chromatic Aberration")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ChromaticAberrationComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter name stable for existing profiles.
#pragma warning disable SA1307
        [Tooltip("Radial RGB separation toward the screen edges. Zero disables the effect.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.ChromaticAberrationIntensity();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}
