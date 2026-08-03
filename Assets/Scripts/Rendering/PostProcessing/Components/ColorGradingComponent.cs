#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable, VolumeComponentMenu("Fodinae/Color Grading")]
    public class ColorGradingComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Exposure compensation in stops. Zero is neutral.")]
        public ClampedFloatParameter exposure = new(0f, -4f, 4f);

        [Tooltip("Multiplicative color filter. White is neutral.")]
        public ColorParameter colorFilter = new(Color.white);

        [Tooltip("Contrast adjustment. Zero is neutral.")]
        public ClampedFloatParameter contrast = new(0f, -1f, 1f);

        [Tooltip("Color saturation. One is neutral, zero is grayscale.")]
        public ClampedFloatParameter saturation = new(1f, 0f, 2f);

        [Tooltip("Enable display-referred HDR tone mapping.")]
        public BoolParameter toneMapping = new(true);

        [Tooltip("HDR luminance mapped to display white. Higher values preserve more highlight range.")]
        public ClampedFloatParameter toneMappingWhitePoint = new(1f, 0.25f, 8f);

        public bool IsActive() => toneMapping.value ||
                                 exposure.value != 0f ||
                                 colorFilter.value != Color.white ||
                                 contrast.value != 0f ||
                                 saturation.value != 1f;
        public bool IsTileCompatible() => true;
    }
}
