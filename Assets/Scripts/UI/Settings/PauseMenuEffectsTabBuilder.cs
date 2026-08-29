#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Effects tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuEffectsTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly PostProcessController _postProcessController;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuEffectsTabBuilder(
        GraphicsSettingsController graphicsSettings,
        PostProcessController postProcessController,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _graphicsSettings = graphicsSettings;
        _postProcessController = postProcessController;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView effectsScroll)
    {
        VisualElement postProcessSection = effectsScroll.Q<VisualElement>("EffectsSection") ??
            throw new InvalidOperationException("[PauseMenu] EffectsSection is missing from PauseMenu.uxml.");
        VisualElement bloomGroup = effectsScroll.Q<VisualElement>("EffectsGroupBloom") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupBloom is missing from PauseMenu.uxml.");
        VisualElement cameraGroup = effectsScroll.Q<VisualElement>("EffectsGroupCamera") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupCamera is missing from PauseMenu.uxml.");
        VisualElement detailGroup = effectsScroll.Q<VisualElement>("EffectsGroupDetail") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupDetail is missing from PauseMenu.uxml.");
        VisualElement opticsGroup = effectsScroll.Q<VisualElement>("EffectsGroupOptics") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupOptics is missing from PauseMenu.uxml.");
        VisualElement atmosphereGroup = effectsScroll.Q<VisualElement>("EffectsGroupAtmosphere") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupAtmosphere is missing from PauseMenu.uxml.");
        VisualElement displayGroup = effectsScroll.Q<VisualElement>("EffectsGroupDisplay") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupDisplay is missing from PauseMenu.uxml.");
        VisualElement temporalGroup = effectsScroll.Q<VisualElement>("EffectsGroupTemporal") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupTemporal is missing from PauseMenu.uxml.");

        _postProcessController.EnsureVolumeSetup();
        void SavePostProcess(Action<ClientConfig> update)
        {
            _graphicsSettings.UpdatePostProcessSettings(update);
        }

        bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.bloom"),
            () => _clientConfig.Config.BloomIntensity,
            value => SavePostProcess(config => config.BloomIntensity = value),
            0f,
            2f,
            _refreshers));
        bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.bloom_threshold"),
            () => _clientConfig.Config.BloomThreshold,
            value => SavePostProcess(config => config.BloomThreshold = value),
            0f,
            2f,
            _refreshers));
        bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.bloom_soft_knee"),
            () => _clientConfig.Config.BloomSoftKnee,
            value => SavePostProcess(config => config.BloomSoftKnee = value),
            0f,
            1f,
            _refreshers));
        bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.bloom_radius"),
            () => _clientConfig.Config.BloomRadius,
            value => SavePostProcess(config => config.BloomRadius = value),
            0.5f,
            8f,
            _refreshers));
        bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.bloom_scatter"),
            () => _clientConfig.Config.BloomScatter,
            value => SavePostProcess(config => config.BloomScatter = value),
            0.1f,
            1f,
            _refreshers));
        bloomGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.effects.bloom_tint"),
            () => _clientConfig.Config.BloomTint,
            value => SavePostProcess(config => config.BloomTint = value),
            0f,
            2f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.vignette"),
            () => _clientConfig.Config.VignetteIntensity,
            value => SavePostProcess(config => config.VignetteIntensity = value),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.vignette_softness"),
            () => _clientConfig.Config.VignetteSmoothness,
            value => SavePostProcess(config => config.VignetteSmoothness = value),
            0.01f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.vignette_center_x"),
            () => _clientConfig.Config.VignetteCenter.x,
            value => SavePostProcess(config =>
                config.VignetteCenter = new Vector2(value, config.VignetteCenter.y)),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.vignette_center_y"),
            () => _clientConfig.Config.VignetteCenter.y,
            value => SavePostProcess(config =>
                config.VignetteCenter = new Vector2(config.VignetteCenter.x, value)),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.effects.vignette_color"),
            () => _clientConfig.Config.VignetteColor,
            value => SavePostProcess(config => config.VignetteColor = value),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.chromatic_aberration"),
            () => _clientConfig.Config.ChromaticAberrationIntensity,
            value => SavePostProcess(
                config => config.ChromaticAberrationIntensity = value),
            0f,
            0.25f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.exposure"),
            () => _clientConfig.Config.ColorGradingExposure,
            value => SavePostProcess(config => config.ColorGradingExposure = value),
            -2f,
            2f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.contrast"),
            () => _clientConfig.Config.ColorGradingContrast,
            value => SavePostProcess(config => config.ColorGradingContrast = value),
            -0.5f,
            0.5f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.saturation"),
            () => _clientConfig.Config.ColorGradingSaturation,
            value => SavePostProcess(config => config.ColorGradingSaturation = value),
            0f,
            2f,
            _refreshers));
        Toggle toneMappingToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.effects.tone_mapping"),
            () => _clientConfig.Config.ColorGradingToneMapping,
            value => SavePostProcess(config => config.ColorGradingToneMapping = value),
            _refreshers);
        cameraGroup.Add(toneMappingToggle);
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.tone_mapping_white_point"),
            () => _clientConfig.Config.ColorGradingToneMappingWhitePoint,
            value => SavePostProcess(
                config => config.ColorGradingToneMappingWhitePoint = value),
            0.25f,
            8f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.effects.color_filter"),
            () => _clientConfig.Config.ColorGradingFilter,
            value => SavePostProcess(config => config.ColorGradingFilter = value),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.grain"),
            () => _clientConfig.Config.EigengrauIntensity,
            value => SavePostProcess(config => config.EigengrauIntensity = value),
            0f,
            0.25f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.effects.grain_color"),
            () => _clientConfig.Config.EigengrauColor,
            value => SavePostProcess(config => config.EigengrauColor = value),
            0f,
            1f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.grain_darkness_threshold"),
            () => _clientConfig.Config.EigengrauDarknessThreshold,
            value => SavePostProcess(config => config.EigengrauDarknessThreshold = value),
            0.02f,
            0.75f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.grain_scale"),
            () => _clientConfig.Config.EigengrauNoiseScale,
            value => SavePostProcess(config => config.EigengrauNoiseScale = value),
            0.75f,
            2f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.grain_speed"),
            () => _clientConfig.Config.EigengrauAnimationSpeed,
            value => SavePostProcess(config => config.EigengrauAnimationSpeed = value),
            1f,
            60f,
            _refreshers));
        cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.motion_blur"),
            () => _clientConfig.Config.MotionBlurIntensity,
            value => SavePostProcess(config => config.MotionBlurIntensity = value),
            0f,
            0.5f,
            _refreshers));

        AdvancedPostProcessSettings Advanced() =>
            _clientConfig.Config.AdvancedPostProcess;
        void SaveAdvanced(Action<AdvancedPostProcessSettings> update)
        {
            SavePostProcess(config => update(config.AdvancedPostProcess));
        }

        detailGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.local_sharpness"),
            () => Advanced().LocalContrastIntensity,
            value => SaveAdvanced(settings => settings.LocalContrastIntensity = value),
            0f,
            0.5f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.visor_dust"),
            () => Advanced().LensDirtIntensity,
            value => SaveAdvanced(settings => settings.LensDirtIntensity = value),
            0f,
            0.35f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.visor_dust_scale"),
            () => Advanced().LensDirtScale,
            value => SaveAdvanced(settings => settings.LensDirtScale = value),
            0.25f,
            16f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.anamorphic_beams"),
            () => Advanced().AnamorphicIntensity,
            value => SaveAdvanced(settings => settings.AnamorphicIntensity = value),
            0f,
            1f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.anamorphic_length"),
            () => Advanced().AnamorphicLength,
            value => SaveAdvanced(settings => settings.AnamorphicLength = value),
            0.25f,
            8f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.chromatic_diffraction"),
            () => Advanced().ChromaticDiffractionIntensity,
            value => SaveAdvanced(
                settings => settings.ChromaticDiffractionIntensity = value),
            0f,
            0.5f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.heat_refraction"),
            () => Advanced().HeatRefractionIntensity,
            value => SaveAdvanced(settings => settings.HeatRefractionIntensity = value),
            0f,
            0.25f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.heat_wave_size"),
            () => Advanced().HeatRefractionScale,
            value => SaveAdvanced(settings => settings.HeatRefractionScale = value),
            0.25f,
            16f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.material_glints"),
            () => Advanced().GlintIntensity,
            value => SaveAdvanced(settings => settings.GlintIntensity = value),
            0f,
            0.5f,
            _refreshers));
        opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.glints_threshold"),
            () => Advanced().GlintThreshold,
            value => SaveAdvanced(settings => settings.GlintThreshold = value),
            0f,
            4f,
            _refreshers));
        atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.glow_dust"),
            () => Advanced().VolumetricDustIntensity,
            value => SaveAdvanced(settings => settings.VolumetricDustIntensity = value),
            0f,
            0.25f,
            _refreshers));
        atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.glow_dust_scale"),
            () => Advanced().VolumetricDustScale,
            value => SaveAdvanced(settings => settings.VolumetricDustScale = value),
            0.1f,
            8f,
            _refreshers));
        atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.glow_dust_speed"),
            () => Advanced().VolumetricDustSpeed,
            value => SaveAdvanced(settings => settings.VolumetricDustSpeed = value),
            0f,
            2f,
            _refreshers));
        displayGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.phosphor_pattern"),
            () => Advanced().PhosphorMaskIntensity,
            value => SaveAdvanced(settings => settings.PhosphorMaskIntensity = value),
            0f,
            0.35f,
            _refreshers));
        displayGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.perceptual_dithering"),
            () => Advanced().DitheringIntensity,
            value => SaveAdvanced(settings => settings.DitheringIntensity = value),
            0f,
            1f,
            _refreshers));
        temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.phosphor_afterglow"),
            () => Advanced().TemporalPersistenceIntensity,
            value => SaveAdvanced(
                settings => settings.TemporalPersistenceIntensity = value),
            0f,
            0.8f,
            _refreshers));
        temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.effects.phosphor_afterglow_decay"),
            () => Advanced().TemporalPersistenceDecay,
            value => SaveAdvanced(
                settings => settings.TemporalPersistenceDecay = value),
            0f,
            0.98f,
            _refreshers));
        temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.temporal_stability"),
            () => Advanced().LightStability,
            value => SaveAdvanced(settings => settings.LightStability = value),
            0f,
            0.9f,
            _refreshers));

        return effectsScroll;
    }
}
