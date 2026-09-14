#nullable enable

using System;
using UnityEngine.Serialization;

namespace Fodinae.Core;

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

    [SettingUnbounded("Тумблер расхождения каналов.")]
    [SettingLabel("settings.effects.chromatic_aberration")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.ChromaticAberrationIntensity")]
    public bool ChromaticAberrationEnabled;

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

    [SettingUnbounded("Тумблер локального контраста.")]
    [SettingLabel("settings.effects.local_sharpness")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> LocalContrast")]
    public bool LocalContrastEnabled;

    [SettingUnbounded("Тумблер оптических дефектов объектива.")]
    [SettingLabel("settings.effects.anamorphic_beams")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> AnamorphicLens")]
    public bool LensEffectsEnabled;

    [SettingUnbounded("Тумблер объёмной пыли и теплового искажения.")]
    [SettingLabel("settings.effects.glow_dust")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> Atmosphere")]
    public bool AtmosphereEnabled;

    [SettingUnbounded("Тумблер временного накопления.")]
    [SettingLabel("settings.effects.phosphor_afterglow")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "AdvancedPostProcessComposer -> TemporalAccumulation")]
    public bool TemporalEnabled;
}
