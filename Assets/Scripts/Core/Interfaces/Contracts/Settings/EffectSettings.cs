#nullable enable

using System;
using UnityEngine.Serialization;

namespace Kern.Core;

[Serializable]
public sealed class EffectSettings
{
    [SettingUnbounded("Тумблер свечения ярких участков.")]
    [SettingLabel("settings.effects.bloom")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.BloomIntensity")]
    public bool BloomEnabled;

    [SettingUnbounded("Тумблер затемнения к краям кадра.")]
    [SettingLabel("settings.effects.vignette")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.VignetteIntensity")]
    public bool VignetteEnabled;

    // Раньше поле называлось FilmGrainEnabled и подписывалось «зерном», хотя
    // включало эйгенграу целиком: зерно рисуется внутри его ветки шейдера.
    [SettingUnbounded("Тумблер эйгенграу — шума и подсветки в тёмных участках.")]
    [SettingLabel("settings.effects.eigengrau")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.EigengrauIntensity")]
    [FormerlySerializedAs("FilmGrainEnabled")]
    public bool EigengrauEnabled;

    [SettingUnbounded("Тумблер смаза движения.")]
    [SettingLabel("settings.effects.motion_blur")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.MotionBlurIntensity")]
    public bool MotionBlurEnabled;
}
