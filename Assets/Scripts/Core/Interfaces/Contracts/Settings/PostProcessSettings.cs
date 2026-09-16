#nullable enable

using System;

namespace Kern.Core;

[Serializable]
public sealed class PostProcessSettings
{
    public const float ExposureMin = -2f;
    public const float ExposureMax = 2f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float SaturationMin = 0f;
    public const float SaturationMax = 2f;
    public const float DefaultExposure = 0f;
    public const float DefaultContrast = 0f;
    public const float DefaultSaturation = 1f;

    [SettingRange(ExposureMin, ExposureMax)]
    [SettingLabel("settings.effects.exposure")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Exposure")]
    public float Exposure = DefaultExposure;

    [SettingRange(ContrastMin, ContrastMax)]
    [SettingLabel("settings.effects.contrast")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Contrast")]
    public float Contrast = DefaultContrast;

    [SettingRange(SaturationMin, SaturationMax)]
    [SettingLabel("settings.effects.saturation")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Saturation")]
    public float Saturation = DefaultSaturation;
}
