#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio;
using Fodinae.Audio.Backend;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting;
using Fodinae.World.Lighting.Quality;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    internal sealed class PauseMenuSettingsBuilder
    {
        private readonly UIDocument _doc;
        private readonly IClientConfigManager _clientConfig;
        private readonly IAudioSystem _audioSystem;
        private readonly DisplayManager _displayManager;
        private readonly GraphicsSettingsController _graphicsSettings;
        private readonly LightingEngine _lightingEngine;
        private readonly PostProcessController _postProcessController;
        private readonly INetworkService _networkService;
        private readonly IConnectionService _connectionService;
        private readonly ILocalPlayerState _localPlayer;

        // Shared with PauseMenu: opening the settings page replays every
        // refresher so each control re-reads its live value instead of showing
        // whatever was current when the menu was first built.
        private readonly ICollection<Action> _refreshers;

        private readonly Action _closeMenu;
        private readonly ILocalizationService _loc;

        private Button? _fullscreenButton;

        // The custom-profile foldout is created on the graphics page but is
        // also opened from technical settings applied elsewhere, so it has to
        // outlive BuildGraphicsPage.
        private Foldout? _customGraphicsSection;
        private Action? _updateLightingQualityButton;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Created before the graphics/advanced pages so the lighting debug
        // controls built alongside the advanced page can be appended to it.
        private Foldout? _debugSection;
#endif

        public PauseMenuSettingsBuilder(
            UIDocument doc,
            IClientConfigManager clientConfig,
            IAudioSystem audioSystem,
            DisplayManager displayManager,
            GraphicsSettingsController graphicsSettings,
            LightingEngine lightingEngine,
            PostProcessController postProcessController,
            INetworkService networkService,
            IConnectionService connectionService,
            ILocalPlayerState localPlayer,
            ICollection<Action> settingsRefreshers,
            Action closeMenu,
            ILocalizationService loc)
        {
            _doc = doc;
            _clientConfig = clientConfig;
            _audioSystem = audioSystem;
            _displayManager = displayManager;
            _graphicsSettings = graphicsSettings;
            _lightingEngine = lightingEngine;
            _postProcessController = postProcessController;
            _networkService = networkService;
            _connectionService = connectionService;
            _localPlayer = localPlayer;
            _refreshers = settingsRefreshers;
            _closeMenu = closeMenu;
            _loc = loc;
        }

        public VisualElement BuildAudioPage(ScrollView audioScroll)
        {
            VisualElement audioSection = audioScroll.Q<VisualElement>("AudioSection") ??
                throw new InvalidOperationException("[PauseMenu] AudioSection is missing from PauseMenu.uxml.");

            audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.master_volume"), AudioBusType.Master));
            audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.sfx_volume"), AudioBusType.SFX));
            audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.music_volume"), AudioBusType.Music));
            audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.ambience_volume"), AudioBusType.Ambience));
            audioSection.Add(CreateAudioSlider(_loc.Get("settings.audio.voice"), AudioBusType.Voice));
            audioSection.Add(CreateAudioSlider(_loc.Get("menu.settings.ui_volume"), AudioBusType.UI));
            Toggle muteInBackgroundToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("menu.settings.mute_background"),
                () => _clientConfig.Config.MuteAudioInBackground,
                value => _clientConfig.UpdateAndSave(
                    config => config.MuteAudioInBackground = value),
                _refreshers);
            audioSection.Add(muteInBackgroundToggle);

            return audioScroll;
        }

        public VisualElement BuildDisplayPage(ScrollView displayScroll)
        {
            VisualElement displaySection = displayScroll.Q<VisualElement>("DisplaySection") ??
                throw new InvalidOperationException("[PauseMenu] DisplaySection is missing from PauseMenu.uxml.");

            _fullscreenButton = new Button(ToggleFullscreen);
            _fullscreenButton.text = Screen.fullScreen ? _loc.Get("menu.settings.fullscreen") : _loc.Get("settings.display.windowed");
            _fullscreenButton.AddToClassList("pause-btn");
            displaySection.Add(_fullscreenButton);

            var resolutions = Screen.resolutions;
            var uniqueResolutions = new List<Resolution>();
            var seen = new HashSet<string>();
            foreach (var res in resolutions)
            {
                var key = $"{res.width}x{res.height}";
                if (seen.Add(key))
                {
                    uniqueResolutions.Add(res);
                }
            }

            int currentResIndex = -1;
            for (int i = 0; i < uniqueResolutions.Count; i++)
            {
                if (uniqueResolutions[i].width == Screen.width &&
                    uniqueResolutions[i].height == Screen.height)
                {
                    currentResIndex = i;
                    break;
                }
            }

            var resolutionButton = new Button();
            void UpdateResolutionButton()
            {
                string resolutionLabel = _loc.Get("menu.settings.resolution");
                resolutionButton.text = uniqueResolutions.Count == 0
                    ? _loc.Get("settings.display.no_resolutions")
                    : currentResIndex >= 0
                        ? $"{resolutionLabel}: {uniqueResolutions[currentResIndex].width} x " +
                          uniqueResolutions[currentResIndex].height
                        : $"{resolutionLabel}: {Screen.width} x {Screen.height}";
            }

            resolutionButton.clicked += () =>
            {
                if (uniqueResolutions.Count > 0)
                {
                    currentResIndex = (currentResIndex + 1) % uniqueResolutions.Count;
                    var resolution = uniqueResolutions[currentResIndex];
                    // Goes through DisplayManager, not Screen.SetResolution
                    // directly - that's the only path that persists the
                    // choice to ClientConfig and normalizes ExclusiveFullScreen
                    // on macOS. A direct Screen.SetResolution call here would
                    // silently revert to the last saved resolution on next
                    // launch, same bug ToggleFullscreen had.
                    _displayManager.SetResolution(
                        resolution.width,
                        resolution.height,
                        Screen.fullScreenMode,
                        (int)resolution.refreshRateRatio.value);
                    UpdateResolutionButton();
                }
            };
            resolutionButton.SetEnabled(uniqueResolutions.Count > 0);
            resolutionButton.AddToClassList("pause-btn");
            UpdateResolutionButton();
            displaySection.Add(resolutionButton);

            // Replaces a "VSync" button that used to live in the Custom
            // graphics profile and edit GraphicsQualitySettings.VSyncCount -
            // a field that is deliberately never applied anywhere (see the
            // remark on LightingEngine.ApplyUnityRenderingSettings:
            // VSync is DisplayManager's alone, to avoid two owners fighting
            // over QualitySettings.vSyncCount). That button compiled, looked
            // like a working control, and did nothing when clicked - this is
            // the real one, wired to the config field DisplayManager actually
            // reads.
            Toggle vSyncToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("menu.settings.vsync"),
                () => _clientConfig.Config.VSync,
                value => _displayManager.SetVSync(value),
                _refreshers);
            displaySection.Add(vSyncToggle);

            return displayScroll;
        }

        public VisualElement BuildGraphicsPage(ScrollView graphicsScroll)
        {
            VisualElement graphicsSection = graphicsScroll.Q<VisualElement>("GraphicsSection") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsSection is missing from PauseMenu.uxml.");

            string[] graphicsPresetNames =
            [
                "settings.preset.very_low",
                "settings.preset.low",
                "settings.preset.medium",
                "settings.preset.high",
                "settings.preset.very_high",
                "settings.preset.ultra",
                "settings.preset.custom",
            ];
            var lightingQuality = new Button();
            void UpdateLightingQualityButton()
            {
                GraphicsPreset selectedPreset = _graphicsSettings.SelectedPreset;
                lightingQuality.text =
                    _loc.Get("settings.graphics.overall_quality") + ": " +
                    _loc.Get(graphicsPresetNames[(int)selectedPreset]);
            }

            _updateLightingQualityButton = UpdateLightingQualityButton;

            lightingQuality.clicked += () =>
            {
                GraphicsPreset currentPreset = _graphicsSettings.SelectedPreset;
                GraphicsPreset nextPreset;
                if (GraphicsQualityProfile.IsStandard(currentPreset))
                {
                    nextPreset = currentPreset == GraphicsPreset.Ultra
                        ? GraphicsPreset.Custom
                        : (GraphicsPreset)((int)currentPreset + 1);
                }
                else
                {
                    nextPreset = GraphicsPreset.VeryLow;
                }

                if (nextPreset == GraphicsPreset.Custom)
                {
                    _graphicsSettings.SelectCustomPreset();
                    if (_customGraphicsSection != null)
                    {
                        _customGraphicsSection.value = true;
                    }
                }
                else
                {
                    _graphicsSettings.SelectStandardPreset(nextPreset);
                }

                RefreshAll();
            };
            lightingQuality.AddToClassList("pause-btn");
            _refreshers.Add(UpdateLightingQualityButton);
            UpdateLightingQualityButton();
            graphicsSection.Add(lightingQuality);

            // Off/PerBlock/PerPixel tier. Standard presets pick this from the
            // GraphicsQualityProfile asset (Ultra is always PerPixel), so the
            // button is read-only outside the Custom profile - cycling it only
            // makes sense once there is somewhere per-player to store the
            // choice.
            string[] lightingQualityTierNames =
            [
                "settings.lighting.per_block",
                "settings.lighting.off",
                "settings.lighting.per_pixel",
                "settings.lighting.per_pixel_bilinear",
            ];
            var lightingQualityTierButton = new Button();
            void UpdateLightingQualityTierButton()
            {
                GraphicsPreset preset = _graphicsSettings.SelectedPreset;
                LightingQualityMode mode = preset == GraphicsPreset.Custom
                    ? _graphicsSettings.CustomSettings.LightingQuality
                    : _lightingEngine.ActiveLightingQuality;
                lightingQualityTierButton.text =
                    _loc.Get("settings.lighting.quality_label") + ": " +
                    _loc.Get(lightingQualityTierNames[(int)mode]);
                lightingQualityTierButton.SetEnabled(preset == GraphicsPreset.Custom);
            }

            lightingQualityTierButton.clicked += () =>
            {
                if (_graphicsSettings.SelectedPreset != GraphicsPreset.Custom)
                {
                    return;
                }

                ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingQuality = settings.LightingQuality switch
                    {
                        LightingQualityMode.Off => LightingQualityMode.PerBlock,
                        LightingQualityMode.PerBlock => LightingQualityMode.PerPixel,
                        LightingQualityMode.PerPixel => LightingQualityMode.PerPixelBilinearFix,
                        _ => LightingQualityMode.Off,
                    };
                    return settings;
                });
                UpdateLightingQualityTierButton();
            };
            lightingQualityTierButton.AddToClassList("pause-btn");
            _refreshers.Add(UpdateLightingQualityTierButton);
            UpdateLightingQualityTierButton();
            graphicsSection.Add(lightingQualityTierButton);

            // Same shape as the lighting tier above: read-only for standard
            // presets, cycleable on Custom. Named by cost rather than by effect
            // list, because "Основное" is the tier that keeps the look and drops
            // bloom and motion blur - the two effects that are most of the
            // stack's cost.
            string[] postProcessTierNames =
            [
                "settings.post_process.full",
                "settings.post_process.off",
                "settings.post_process.core",
            ];
            var postProcessTierButton = new Button();
            void UpdatePostProcessTierButton()
            {
                GraphicsPreset preset = _graphicsSettings.SelectedPreset;

                // No resolver to consult, unlike the lighting tier: nothing
                // overrides this value per preset, so the stored settings are
                // the active settings whichever preset is selected.
                PostProcessQualityMode mode =
                    _clientConfig.Config.GraphicsQualitySettings.PostProcessQuality;
                postProcessTierButton.text =
                    _loc.Get("settings.post_process.quality_label") + ": " +
                    _loc.Get(postProcessTierNames[(int)mode]);
                postProcessTierButton.SetEnabled(preset == GraphicsPreset.Custom);
            }

            postProcessTierButton.clicked += () =>
            {
                if (_graphicsSettings.SelectedPreset != GraphicsPreset.Custom)
                {
                    return;
                }

                ApplyCustomTechnicalSettings(settings =>
                {
                    settings.PostProcessQuality = settings.PostProcessQuality switch
                    {
                        PostProcessQualityMode.Off => PostProcessQualityMode.Essential,
                        PostProcessQualityMode.Essential => PostProcessQualityMode.Full,
                        _ => PostProcessQualityMode.Off,
                    };
                    return settings;
                });
                UpdatePostProcessTierButton();
            };
            postProcessTierButton.AddToClassList("pause-btn");
            _refreshers.Add(UpdatePostProcessTierButton);
            UpdatePostProcessTierButton();
            graphicsSection.Add(postProcessTierButton);

            Toggle distortionToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("settings.world.block_edge_distortion"),
                () => _clientConfig.Config.EnableTerrainDistortion,
                value => _graphicsSettings.UpdateCustomWorldMaterialSettings(
                    config => config.EnableTerrainDistortion = value),
                _refreshers);
            graphicsSection.Add(distortionToggle);

            var customGraphicsSection = new Foldout
            {
                text = _loc.Get("settings.graphics.custom_profile"),
                value = _graphicsSettings.SelectedPreset == GraphicsPreset.Custom,
            };
            customGraphicsSection.AddToClassList("settings-section");
            customGraphicsSection.AddToClassList("settings-section--custom");
            _customGraphicsSection = customGraphicsSection;

            var customGraphicsButton = new Button
            {
                text = _loc.Get("settings.graphics.customize"),
            };
            customGraphicsButton.AddToClassList("pause-btn");
            customGraphicsButton.clicked += () =>
            {
                _graphicsSettings.SelectCustomPreset();
                customGraphicsSection.value = true;
                RefreshAll();
            };
            graphicsSection.Add(customGraphicsButton);

            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.density"),
                () => _graphicsSettings.CustomSettings.LightingMinimumPixelsPerCell,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMinimumPixelsPerCell = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                8f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.max_size"),
                () => _graphicsSettings.CustomSettings.LightingMaximumTextureDimension,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumTextureDimension = Mathf.RoundToInt(value);
                    return settings;
                }),
                GraphicsQualitySettings.MinimumLightingTextureDimension,
                4096f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.max_dynamic_lights"),
                () => _graphicsSettings.CustomSettings.LightingMaximumLightCount,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumLightCount = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                2048f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.cascade_steps"),
                () => _graphicsSettings.CustomSettings.LightingMaximumRaySteps,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumRaySteps = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                128f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.solve_rate"),
                () => _graphicsSettings.CustomSettings.LightingUpdatesPerSecond,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingUpdatesPerSecond = Mathf.Round(value);
                    return settings;
                }),
                1f,
                60f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.lighting.atlas_size"),
                () => _graphicsSettings.CustomSettings.LightingCascadeAtlasLimit,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingCascadeAtlasLimit = Mathf.RoundToInt(value);
                    return settings;
                }),
                128f,
                4096f,
                _refreshers));
            customGraphicsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Render scale",
                () => _graphicsSettings.CustomSettings.RenderScale,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.RenderScale = value;
                    return settings;
                }),
                0.5f,
                1f,
                _refreshers));

            var customAntiAliasingButton = new Button();
            void RefreshCustomAntiAliasing()
            {
                customAntiAliasingButton.text =
                    $"MSAA: {_graphicsSettings.CustomSettings.AntiAliasing}";
            }

            customAntiAliasingButton.clicked += () => ApplyCustomTechnicalSettings(settings =>
            {
                settings.AntiAliasing = settings.AntiAliasing switch
                {
                    0 => 2,
                    2 => 4,
                    4 => 8,
                    _ => 0,
                };
                return settings;
            });
            customAntiAliasingButton.AddToClassList("pause-btn");
            _refreshers.Add(RefreshCustomAntiAliasing);
            RefreshCustomAntiAliasing();
            customGraphicsSection.Add(customAntiAliasingButton);

            graphicsSection.Add(customGraphicsSection);

            Toggle ambientOcclusionToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("settings.advanced.contact_ao"),
                () => _lightingEngine.AmbientOcclusionEnabled,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetAmbientOcclusionEnabled(value);
                },
                _refreshers);
            graphicsSection.Add(ambientOcclusionToggle);

            Toggle globalIlluminationToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("settings.advanced.diffuse_bounce"),
                () => _lightingEngine.DiffuseBounceEnabled,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetDiffuseBounceEnabled(value);
                },
                _refreshers);
            graphicsSection.Add(globalIlluminationToggle);

            return graphicsScroll;
        }

        public VisualElement BuildEffectsPage(ScrollView effectsScroll)
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

        public VisualElement BuildInterfacePage(ScrollView interfaceScroll)
        {
            VisualElement interfaceSection = interfaceScroll.Q<VisualElement>("InterfaceSection") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceSection is missing from PauseMenu.uxml.");

            interfaceSection.Add(PauseMenuUIFactory.CreateSlider(
                _loc.Get("menu.settings.ui_scale"),
                _clientConfig.Config.UIScale,
                v =>
                {
                    _clientConfig.UpdateAndSave(config => config.UIScale = v);

                    // The panel scale is what actually resizes the live UI;
                    // saving alone would only take effect on the next launch.
                    if (_doc != null && _doc.panelSettings != null)
                    {
                        _doc.panelSettings.scale = v;
                    }
                },
                0.5f,
                2f));

            // Язык интерфейса. Применяется сразу: SetLanguage сохраняет выбор
            // в конфиг и стреляет OnLanguageChanged, на который подписаны все
            // экраны — они пересобирают свои тексты (PauseMenu пересобирает
            // дерево целиком через ApplyLocalizedText).
            var languageRow = new VisualElement();
            languageRow.AddToClassList("pause-slider-container");
            var languageLabel = new Label(_loc.Get("settings.interface.language"));
            languageLabel.AddToClassList("pause-slider-label");
            languageRow.Add(languageLabel);

            var languageDropdown = new DropdownField();
            languageDropdown.choices = new System.Collections.Generic.List<string>
            {
                _loc.Get("settings.interface.language.ru"),
                _loc.Get("settings.interface.language.en"),
            };
            languageDropdown.index = _loc.CurrentLanguage == "en" ? 1 : 0;
            languageDropdown.RegisterValueChangedCallback(_ =>
            {
                string code = languageDropdown.index == 1 ? "en" : "ru";
                if (code != _loc.CurrentLanguage)
                {
                    _loc.SetLanguage(code);
                }
            });
            _refreshers.Add(() =>
            {
                languageDropdown.index = _loc.CurrentLanguage == "en" ? 1 : 0;
            });
            languageRow.Add(languageDropdown);
            interfaceSection.Add(languageRow);

            return interfaceScroll;
        }

        public VisualElement BuildAdvancedPage(ScrollView advancedScroll)
        {
            Foldout advancedGraphicsSection = advancedScroll.Q<Foldout>("AdvancedLightingSection") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedLightingSection is missing from PauseMenu.uxml.");
            VisualElement ambientGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupAmbient") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupAmbient is missing from PauseMenu.uxml.");
            VisualElement dynamicGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupDynamic") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupDynamic is missing from PauseMenu.uxml.");
            VisualElement extinctionGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupExtinction") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupExtinction is missing from PauseMenu.uxml.");
            VisualElement bounceGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupBounce") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupBounce is missing from PauseMenu.uxml.");
            VisualElement aoGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupAO") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupAO is missing from PauseMenu.uxml.");
            VisualElement boundsGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupBounds") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedGroupBounds is missing from PauseMenu.uxml.");
            VisualElement worldMaterialsSection = advancedScroll.Q<VisualElement>("WorldMaterialsSection") ??
                throw new InvalidOperationException("[PauseMenu] WorldMaterialsSection is missing from PauseMenu.uxml.");

            void ApplyLightingSetting(
                float value,
                Action<LightingEngine, float> apply)
            {
                MarkGraphicsCustom();
                apply(_lightingEngine, value);
            }

            float GetLightingValue(Func<LightingEngine, float> actualValue)
            {
                return actualValue(_lightingEngine);
            }

            void ApplyLightingColor(
                Color value,
                Action<LightingEngine, Color> apply)
            {
                MarkGraphicsCustom();
                apply(_lightingEngine, value);
            }

            ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.ambient_intensity"),
                () => GetLightingValue(static engine => engine.AmbientIntensity),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                1f,
                _refreshers));
            ambientGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.advanced.ambient_color"),
                () => _lightingEngine.AmbientColor,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetAmbientColor(setting)),
                0f,
                4f,
                _refreshers));
            ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.emission_power"),
                () => GetLightingValue(static engine => engine.EmissionScale),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                8f,
                _refreshers));

            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.player_emission_power"),
                () => ResolveLocalRobot()?.DynamicLightIntensity ?? 0f,
                value =>
                {
                    MarkGraphicsCustom();
                    ResolveLocalRobot()?.SetDynamicLightIntensity(value);
                },
                0f,
                4f,
                _refreshers));
            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.dynamic_emission_rate"),
                () => _lightingEngine.DynamicLightUpdatesPerSecond,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetDynamicLightUpdatesPerSecond(value);
                },
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond,
                _refreshers));

            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.light_red"),
                () => ResolveLocalRobot()?.DynamicLightColor.r ?? 0f,
                value =>
                {
                    MarkGraphicsCustom();
                    Robot? localRobot = ResolveLocalRobot();
                    if (localRobot == null)
                    {
                        return;
                    }

                    Color color = localRobot.DynamicLightColor;
                    localRobot.SetDynamicLightColor(new Color(value, color.g, color.b, 1f));
                },
                0f,
                1f,
                _refreshers));
            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.light_green"),
                () => ResolveLocalRobot()?.DynamicLightColor.g ?? 0f,
                value =>
                {
                    MarkGraphicsCustom();
                    Robot? localRobot = ResolveLocalRobot();
                    if (localRobot == null)
                    {
                        return;
                    }

                    Color color = localRobot.DynamicLightColor;
                    localRobot.SetDynamicLightColor(new Color(color.r, value, color.b, 1f));
                },
                0f,
                1f,
                _refreshers));
            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.light_blue"),
                () => ResolveLocalRobot()?.DynamicLightColor.b ?? 0f,
                value =>
                {
                    MarkGraphicsCustom();
                    Robot? localRobot = ResolveLocalRobot();
                    if (localRobot == null)
                    {
                        return;
                    }

                    Color color = localRobot.DynamicLightColor;
                    localRobot.SetDynamicLightColor(new Color(color.r, color.g, value, 1f));
                },
                0f,
                1f,
                _refreshers));

            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.advanced.empty_extinction"),
                () => _lightingEngine.EmptyExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetEmptyExtinctionColor(setting)),
                0f,
                4f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.advanced.solid_extinction"),
                () => _lightingEngine.SolidExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetSolidExtinctionColor(setting)),
                0f,
                4f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.empty_extinction_falloff"),
                () => GetLightingValue(static engine => engine.EmptyExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                2f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.solid_extinction_falloff"),
                () => GetLightingValue(static engine => engine.SolidExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                2f,
                _refreshers));
            bounceGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.bounce_strength"),
                () => GetLightingValue(static engine => engine.BounceStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                1f,
                _refreshers));
            aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.ao_radius"),
                () => GetLightingValue(static engine => engine.AmbientOcclusionRadiusCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f,
                _refreshers));
            aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.ao_strength"),
                () => GetLightingValue(static engine => engine.AmbientOcclusionStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f,
                _refreshers));
            VisualElement maximumLightMultiplierSlider = PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.max_light_multiplier"),
                () => GetLightingValue(static engine => engine.MaximumLightMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier,
                _refreshers);
            void RefreshMaximumLightMultiplierState()
            {
                maximumLightMultiplierSlider.SetEnabled(_lightingEngine.EnableFinalLightingClamp);
            }

            _refreshers.Add(RefreshMaximumLightMultiplierState);
            RefreshMaximumLightMultiplierState();
            boundsGroup.Add(maximumLightMultiplierSlider);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.transmittance_debug"),
                () => GetLightingValue(static engine => engine.TransmittanceDebugDistanceCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetTransmittanceDebugDistance(setting)),
                2f,
                32f,
                _refreshers));
#endif
            boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.min_transmission"),
                () => GetLightingValue(static engine => engine.MinimumTransmission),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetMinimumTransmission(setting)),
                0.0001f,
                0.1f,
                _refreshers));
            boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.advanced.light_safe_border"),
                () => _lightingEngine.LightSafeBorder,
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetLightSafeBorder(setting)),
                0f,
                8f,
                _refreshers));
            Toggle finalLightingClampToggle = PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get("settings.advanced.clamp_final_light"),
                () => _lightingEngine.EnableFinalLightingClamp,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetFinalLightingClampEnabled(value);
                    RefreshMaximumLightMultiplierState();
                },
                _refreshers);
            boundsGroup.Add(finalLightingClampToggle);

            void SaveShaderSetting(Action<ClientConfig> update)
            {
                _graphicsSettings.UpdateCustomWorldMaterialSettings(update);
            }

            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.world.shimmer_speed"),
                () => _clientConfig.Config.TerrainShimmerSpeedScale,
                value => SaveShaderSetting(
                    config => config.TerrainShimmerSpeedScale = value),
                0f,
                10f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.world.shimmer_color"),
                () => _clientConfig.Config.TerrainShimmerColor,
                value => SaveShaderSetting(config => config.TerrainShimmerColor = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.world.pulse_speed"),
                () => _clientConfig.Config.TerrainPulseSpeedScale,
                value => SaveShaderSetting(config => config.TerrainPulseSpeedScale = value),
                0f,
                10f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.world.surface_emission"),
                () => _clientConfig.Config.TransitEmissionStrength,
                value => SaveShaderSetting(config => config.TransitEmissionStrength = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.world.surface_emission_color"),
                () => _clientConfig.Config.TransitEmissionColor,
                value => SaveShaderSetting(config => config.TransitEmissionColor = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                _loc.Get("settings.world.far_surface_emission"),
                () => _clientConfig.Config.PerspectiveEmissionStrength,
                value => SaveShaderSetting(
                    config => config.PerspectiveEmissionStrength = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                _loc.Get("settings.world.far_surface_color"),
                () => _clientConfig.Config.PerspectiveEmissionColor,
                value => SaveShaderSetting(
                    config => config.PerspectiveEmissionColor = value),
                0f,
                8f,
                _refreshers));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AddLightingDebugControls();
#endif

            return advancedScroll;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Creates the developer tools foldout. Must be called before
        /// <see cref="BuildAdvancedPage"/>, which appends the lighting debug
        /// view and the live diagnostics readout to it.
        /// </summary>
        public Foldout BuildDebugSection()
        {
            var debugSection = new Foldout
            {
                text = _loc.Get("settings.debug.tools"),
                value = false,
            };
            debugSection.AddToClassList("settings-section");
            debugSection.AddToClassList("settings-section--debug");
            _debugSection = debugSection;

            debugSection.Add(PauseMenuUIFactory.CreateLabel(_loc.Get("settings.debug.tools")));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_kick"), () =>
            {
                _connectionService.TriggerDisconnect(_loc.Get("settings.debug.test_disconnect"));
                _closeMenu();
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_reconnect"), () =>
            {
                _connectionService.TriggerReconnect(_loc.Get("settings.debug.server_restart"));
                _closeMenu();
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_open_url"), () =>
            {
                SendElementClick("open_url_test");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_modal"), () =>
            {
                SendElementClick("test_modal");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.join_clan"), () =>
            {
                SendElementClick("join_clan");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.leave_clan"), () =>
            {
                SendElementClick("leave_clan");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_mission_arrow"), () =>
            {
                SendElementClick("test_mission_arrow");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.missions"), () =>
            {
                SendElementClick("open_missions");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.walls_off"), () =>
            {
                ILocalPlayer? player = _localPlayer.Current;
                if (player != null)
                {
                    player.IgnoreCollision = !player.IgnoreCollision;
                    _closeMenu();
                }
            }));

            return debugSection;
        }

        private void SendElementClick(string tag)
        {
            _networkService.Send(new ElementClickPacket(tag, 0, Array.Empty<StringPairPacket>()));
            _closeMenu();
        }

        private void AddLightingDebugControls()
        {
            Foldout? debugSection = _debugSection;
            if (debugSection == null)
            {
                return;
            }

            string[] lightingDebugNames =
            [
                "settings.debug.final_lighting",
                "settings.debug.occupancy",
                "settings.debug.albedo",
                "settings.debug.emission",
                "settings.debug.transmission",
                "settings.debug.direct_radiance",
                "settings.debug.diffuse_bounce",
                "settings.debug.contact_occlusion",
                "settings.debug.exposure",
            ];
            int activeDebugView = (int)_lightingEngine.ActiveDebugView;
            var lightingDebugView = new Button();
            void UpdateLightingDebugButton()
            {
                lightingDebugView.text =
                    _loc.Get("settings.debug.lighting_label") + ": " +
                    _loc.Get(lightingDebugNames[activeDebugView]);
            }

            lightingDebugView.clicked += () =>
            {
                activeDebugView = (activeDebugView + 1) % lightingDebugNames.Length;
                _lightingEngine.SetDebugView(
                    (LightingEngine.DebugView)activeDebugView);

                UpdateLightingDebugButton();
            };
            lightingDebugView.AddToClassList("pause-btn");
            UpdateLightingDebugButton();
            debugSection.Add(lightingDebugView);

            debugSection.Add(PauseMenuUIFactory.CreateLabel(_loc.Get("settings.lighting.actual_params")));
            var lightingDiagnostics = new Label();
            lightingDiagnostics.AddToClassList("pause-slider-label");
            void UpdateLightingDiagnostics()
            {
                lightingDiagnostics.text =
                    $"Quality={_lightingEngine.ActiveGraphicsPreset}\n" +
                    $"Config={_lightingEngine.RuntimeConfigFilePath}\n" +
                    $"Debug={_lightingEngine.ActiveDebugView}\n" +
                    $"AO={(_lightingEngine.AmbientOcclusionEnabled ? 1 : 0)} " +
                    $"radius={_lightingEngine.AmbientOcclusionRadiusCells:F2} " +
                    $"strength={_lightingEngine.AmbientOcclusionStrength:F2}\n" +
                    $"DiffuseBounce={(_lightingEngine.DiffuseBounceEnabled ? 1 : 0)} " +
                    $"strength={_lightingEngine.BounceStrength:F3}\n" +
                    $"Ambient={_lightingEngine.AmbientIntensity:F3} " +
                    $"Emission={_lightingEngine.EmissionScale:F3} " +
                    $"DynamicRate={_lightingEngine.DynamicLightUpdatesPerSecond:F1}\n" +
                    $"EmptyExtinction={_lightingEngine.EmptyExtinctionMultiplier:F3} " +
                    $"SolidExtinction={_lightingEngine.SolidExtinctionMultiplier:F3}\n" +
                    $"MinimumTransmission={_lightingEngine.MinimumTransmission:F4} " +
                    $"MaximumLight={_lightingEngine.MaximumLightMultiplier:F3}\n" +
                    $"SafeBorder={_lightingEngine.LightSafeBorder} " +
                    $"TransmissionDistance={_lightingEngine.TransmittanceDebugDistanceCells:F2}\n" +
                    $"Field={_lightingEngine.FieldWidth}x{_lightingEngine.FieldHeight} " +
                    $"AtlasEntries={_lightingEngine.AtlasEntryCount} " +
                    $"DynamicLights={_lightingEngine.DynamicLightCount} " +
                    $"Uploaded={_lightingEngine.UploadedDynamicLightCount} " +
                    $"Dropped={_lightingEngine.DroppedDynamicLightCount} " +
                    $"DroppedIds=[{string.Join(",", _lightingEngine.DroppedDynamicLightIds)}]\n" +
                    $"ComputeAmbient={_lightingEngine.ComputeAmbientColor} " +
                    $"ComputeEmptyExtinction={_lightingEngine.ComputeEmptyExtinction} " +
                    $"ComputeSolidExtinction={_lightingEngine.ComputeSolidExtinction}\n" +
                    $"RequiredPadding={_lightingEngine.RequiredTerrainPadding} " +
                    $"SolveCount={_lightingEngine.SolveCount} " +
                    $"ContactAOSolveCount={_lightingEngine.ContactOcclusionSolveCount}";
            }

            UpdateLightingDiagnostics();
            debugSection.Add(lightingDiagnostics);
            var refreshLightingDiagnostics = new Button(UpdateLightingDiagnostics)
            {
                text = _loc.Get("settings.lighting.refresh"),
            };
            refreshLightingDiagnostics.AddToClassList("pause-btn");
            debugSection.Add(refreshLightingDiagnostics);
            var resetLightingPreferences = new Button(() =>
            {
                MarkGraphicsCustom();
                _lightingEngine.ResetRuntimeLightingPreferences();
                ResolveLocalRobot()?.ResetDynamicLightPreferences();
                RefreshAll();
                UpdateLightingDiagnostics();
            })
            {
                text = _loc.Get("settings.lighting.reset"),
            };
            resetLightingPreferences.AddToClassList("pause-btn");
            debugSection.Add(resetLightingPreferences);
        }
#endif

        private Robot? ResolveLocalRobot()
        {
            return _localPlayer.Current?.GetComponent<Robot>();
        }

        private void MarkGraphicsCustom()
        {
            _graphicsSettings.MarkCustom();
            _updateLightingQualityButton?.Invoke();
        }

        private void ApplyCustomTechnicalSettings(
            Func<GraphicsQualitySettings, GraphicsQualitySettings> update)
        {
            _graphicsSettings.MarkCustom();
            GraphicsQualitySettings settings = update(_graphicsSettings.CustomSettings);
            _graphicsSettings.SetCustomSettings(settings);
            if (_customGraphicsSection != null)
            {
                _customGraphicsSection.value = true;
            }

            _updateLightingQualityButton?.Invoke();
        }

        private void RefreshAll()
        {
            // Copied first: a refresher may add another control on a page that
            // has not been built yet, and mutating the shared list mid-iteration
            // would throw.
            var snapshot = new List<Action>(_refreshers);
            foreach (Action refresh in snapshot)
            {
                refresh();
            }
        }

        private void ToggleFullscreen()
        {
            // Goes through DisplayManager for the same reason the resolution
            // button does: a bare `Screen.fullScreen = ...` assignment never
            // reaches ClientConfig, so the choice silently reverts to
            // whatever FullScreenMode was last saved the next time
            // DisplayManager.ApplyDisplaySettings runs on launch.
            FullScreenMode nextMode = Screen.fullScreenMode == FullScreenMode.Windowed
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
            _displayManager.SetResolution(
                Screen.width,
                Screen.height,
                nextMode,
                (int)Screen.currentResolution.refreshRateRatio.value);
            if (_fullscreenButton != null)
            {
                _fullscreenButton.text = nextMode == FullScreenMode.Windowed
                    ? _loc.Get("settings.display.windowed")
                    : _loc.Get("menu.settings.fullscreen");
            }
        }

        private VisualElement CreateAudioSlider(string title, AudioBusType busType)
        {
            float currentVol = GetConfiguredBusVolume(busType);
            return PauseMenuUIFactory.CreateSlider(
                title,
                currentVol,
                v =>
                {
                    if (_audioSystem is AudioSystem audioSystem && audioSystem.IsInitialized)
                    {
                        audioSystem.SetBusVolume(busType, v);
                    }

                    SetBusVolumeInConfig(busType, v);
                },
                0f,
                1f);
        }

        private float GetConfiguredBusVolume(AudioBusType busType)
        {
            if (_clientConfig == null || _clientConfig.Config == null)
            {
                return 1f;
            }

            return busType switch
            {
                AudioBusType.Master => _clientConfig.Config.MasterVolume,
                AudioBusType.SFX => _clientConfig.Config.SfxVolume,
                AudioBusType.Music => _clientConfig.Config.MusicVolume,
                AudioBusType.Voice => _clientConfig.Config.VoiceVolume,
                AudioBusType.Ambience => _clientConfig.Config.AmbienceVolume,
                AudioBusType.UI => _clientConfig.Config.UIVolume,
                _ => throw new ArgumentOutOfRangeException(nameof(busType), busType, "Unsupported audio bus."),
            };
        }

        private void SetBusVolumeInConfig(AudioBusType busType, float volume)
        {
            _clientConfig.UpdateAndSave(config =>
            {
                switch (busType)
                {
                    case AudioBusType.Master:
                        config.MasterVolume = volume;
                        break;
                    case AudioBusType.SFX:
                        config.SfxVolume = volume;
                        break;
                    case AudioBusType.Music:
                        config.MusicVolume = volume;
                        break;
                    case AudioBusType.Voice:
                        config.VoiceVolume = volume;
                        break;
                    case AudioBusType.Ambience:
                        config.AmbienceVolume = volume;
                        break;
                    case AudioBusType.UI:
                        config.UIVolume = volume;
                        break;
                }
            });
        }
    }
}
