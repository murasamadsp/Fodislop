#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio;
using Fodinae.Audio.Backend;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
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

        // Shared with PauseMenu: opening the settings page replays every
        // refresher so each control re-reads its live value instead of showing
        // whatever was current when the menu was first built.
        private readonly ICollection<Action> _refreshers;

        private readonly Action _closeMenu;

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
            ICollection<Action> settingsRefreshers,
            Action closeMenu)
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
            _refreshers = settingsRefreshers;
            _closeMenu = closeMenu;
        }

        public VisualElement BuildAudioPage(ScrollView audioScroll)
        {
            VisualElement audioSection = audioScroll.Q<VisualElement>("AudioSection") ??
                throw new InvalidOperationException("[PauseMenu] AudioSection is missing from PauseMenu.uxml.");

            audioSection.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master));
            audioSection.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX));
            audioSection.Add(CreateAudioSlider("Музыка", AudioBusType.Music));
            audioSection.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience));
            audioSection.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice));
            audioSection.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI));
            Toggle muteInBackgroundToggle = PauseMenuUIFactory.CreateBoundToggle(
                "Глушить звук в фоне",
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
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
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
                resolutionButton.text = uniqueResolutions.Count == 0
                    ? "Разрешения недоступны"
                    : currentResIndex >= 0
                        ? $"Разрешение: {uniqueResolutions[currentResIndex].width} x " +
                          uniqueResolutions[currentResIndex].height
                        : $"Разрешение: {Screen.width} x {Screen.height}";
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
                "Вертикальная синхронизация",
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
                "Очень низкое",
                "Низкое",
                "Среднее",
                "Высокое",
                "Очень высокое",
                "Ультра",
                "Пользовательское",
            ];
            var lightingQuality = new Button();
            void UpdateLightingQualityButton()
            {
                GraphicsPreset selectedPreset = _graphicsSettings.SelectedPreset;
                lightingQuality.text =
                    $"Общее качество графики: {graphicsPresetNames[(int)selectedPreset]}";
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
                "Поблоковое",
                "Выключено",
                "Попиксельное",
                "Попиксельное + Bilinear Fix",
            ];
            var lightingQualityTierButton = new Button();
            void UpdateLightingQualityTierButton()
            {
                GraphicsPreset preset = _graphicsSettings.SelectedPreset;
                LightingQualityMode mode = preset == GraphicsPreset.Custom
                    ? _graphicsSettings.CustomSettings.LightingQuality
                    : _lightingEngine.ActiveLightingQuality;
                lightingQualityTierButton.text =
                    $"Качество освещения: {lightingQualityTierNames[(int)mode]}";
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
                "Полная",
                "Выключена",
                "Основное",
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
                    $"Пост-обработка: {postProcessTierNames[(int)mode]}";
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
                "Дисторсия граней блоков",
                () => _clientConfig.Config.EnableTerrainDistortion,
                value => _graphicsSettings.UpdateCustomWorldMaterialSettings(
                    config => config.EnableTerrainDistortion = value),
                _refreshers);
            graphicsSection.Add(distortionToggle);

            var customGraphicsSection = new Foldout
            {
                text = "Пользовательский профиль",
                value = _graphicsSettings.SelectedPreset == GraphicsPreset.Custom,
            };
            customGraphicsSection.AddToClassList("settings-section");
            customGraphicsSection.AddToClassList("settings-section--custom");
            _customGraphicsSection = customGraphicsSection;

            var customGraphicsButton = new Button
            {
                text = "Настроить пользовательскую графику",
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
                "Плотность lighting",
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
                "Максимальный размер lighting",
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
                "Максимум dynamic lights",
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
                "Шаги lighting cascade",
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
                "Частота lighting solve",
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
                "Размер cascade atlas",
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
                "Контактное затенение (AO)",
                () => _lightingEngine.AmbientOcclusionEnabled,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetAmbientOcclusionEnabled(value);
                },
                _refreshers);
            graphicsSection.Add(ambientOcclusionToggle);

            Toggle globalIlluminationToggle = PauseMenuUIFactory.CreateBoundToggle(
                "Непрямой диффузный свет",
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
                "Свечение",
                () => _clientConfig.Config.BloomIntensity,
                value => SavePostProcess(config => config.BloomIntensity = value),
                0f,
                2f,
                _refreshers));
            bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Порог свечения",
                () => _clientConfig.Config.BloomThreshold,
                value => SavePostProcess(config => config.BloomThreshold = value),
                0f,
                2f,
                _refreshers));
            bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Мягкость порога свечения",
                () => _clientConfig.Config.BloomSoftKnee,
                value => SavePostProcess(config => config.BloomSoftKnee = value),
                0f,
                1f,
                _refreshers));
            bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Радиус свечения",
                () => _clientConfig.Config.BloomRadius,
                value => SavePostProcess(config => config.BloomRadius = value),
                0.5f,
                8f,
                _refreshers));
            bloomGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Рассеивание свечения",
                () => _clientConfig.Config.BloomScatter,
                value => SavePostProcess(config => config.BloomScatter = value),
                0.1f,
                1f,
                _refreshers));
            bloomGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет свечения",
                () => _clientConfig.Config.BloomTint,
                value => SavePostProcess(config => config.BloomTint = value),
                0f,
                2f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Виньетка",
                () => _clientConfig.Config.VignetteIntensity,
                value => SavePostProcess(config => config.VignetteIntensity = value),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Мягкость виньетки",
                () => _clientConfig.Config.VignetteSmoothness,
                value => SavePostProcess(config => config.VignetteSmoothness = value),
                0.01f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Центр виньетки X",
                () => _clientConfig.Config.VignetteCenter.x,
                value => SavePostProcess(config =>
                    config.VignetteCenter = new Vector2(value, config.VignetteCenter.y)),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Центр виньетки Y",
                () => _clientConfig.Config.VignetteCenter.y,
                value => SavePostProcess(config =>
                    config.VignetteCenter = new Vector2(config.VignetteCenter.x, value)),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет виньетки",
                () => _clientConfig.Config.VignetteColor,
                value => SavePostProcess(config => config.VignetteColor = value),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Хроматическая аберрация",
                () => _clientConfig.Config.ChromaticAberrationIntensity,
                value => SavePostProcess(
                    config => config.ChromaticAberrationIntensity = value),
                0f,
                0.25f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Экспозиция",
                () => _clientConfig.Config.ColorGradingExposure,
                value => SavePostProcess(config => config.ColorGradingExposure = value),
                -2f,
                2f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Контраст",
                () => _clientConfig.Config.ColorGradingContrast,
                value => SavePostProcess(config => config.ColorGradingContrast = value),
                -0.5f,
                0.5f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Насыщенность",
                () => _clientConfig.Config.ColorGradingSaturation,
                value => SavePostProcess(config => config.ColorGradingSaturation = value),
                0f,
                2f,
                _refreshers));
            Toggle toneMappingToggle = PauseMenuUIFactory.CreateBoundToggle(
                "Тональное отображение",
                () => _clientConfig.Config.ColorGradingToneMapping,
                value => SavePostProcess(config => config.ColorGradingToneMapping = value),
                _refreshers);
            cameraGroup.Add(toneMappingToggle);
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Белая точка tone mapping",
                () => _clientConfig.Config.ColorGradingToneMappingWhitePoint,
                value => SavePostProcess(
                    config => config.ColorGradingToneMappingWhitePoint = value),
                0.25f,
                8f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цветовой фильтр",
                () => _clientConfig.Config.ColorGradingFilter,
                value => SavePostProcess(config => config.ColorGradingFilter = value),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Зернистость",
                () => _clientConfig.Config.EigengrauIntensity,
                value => SavePostProcess(config => config.EigengrauIntensity = value),
                0f,
                0.25f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет зернистости",
                () => _clientConfig.Config.EigengrauColor,
                value => SavePostProcess(config => config.EigengrauColor = value),
                0f,
                1f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Порог темноты зернистости",
                () => _clientConfig.Config.EigengrauDarknessThreshold,
                value => SavePostProcess(config => config.EigengrauDarknessThreshold = value),
                0.02f,
                0.75f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Масштаб зернистости",
                () => _clientConfig.Config.EigengrauNoiseScale,
                value => SavePostProcess(config => config.EigengrauNoiseScale = value),
                0.75f,
                2f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Скорость зернистости",
                () => _clientConfig.Config.EigengrauAnimationSpeed,
                value => SavePostProcess(config => config.EigengrauAnimationSpeed = value),
                1f,
                60f,
                _refreshers));
            cameraGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Размытие движения",
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
                "Локальная чёткость",
                () => Advanced().LocalContrastIntensity,
                value => SaveAdvanced(settings => settings.LocalContrastIntensity = value),
                0f,
                0.5f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Световая пыль на визоре",
                () => Advanced().LensDirtIntensity,
                value => SaveAdvanced(settings => settings.LensDirtIntensity = value),
                0f,
                0.35f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Масштаб световой пыли",
                () => Advanced().LensDirtScale,
                value => SaveAdvanced(settings => settings.LensDirtScale = value),
                0.25f,
                16f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Анаморфные лучи",
                () => Advanced().AnamorphicIntensity,
                value => SaveAdvanced(settings => settings.AnamorphicIntensity = value),
                0f,
                1f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Длина анаморфных лучей",
                () => Advanced().AnamorphicLength,
                value => SaveAdvanced(settings => settings.AnamorphicLength = value),
                0.25f,
                8f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Хроматическая дифракция",
                () => Advanced().ChromaticDiffractionIntensity,
                value => SaveAdvanced(
                    settings => settings.ChromaticDiffractionIntensity = value),
                0f,
                0.5f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Тепловая рефракция",
                () => Advanced().HeatRefractionIntensity,
                value => SaveAdvanced(settings => settings.HeatRefractionIntensity = value),
                0f,
                0.25f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Размер тепловых волн",
                () => Advanced().HeatRefractionScale,
                value => SaveAdvanced(settings => settings.HeatRefractionScale = value),
                0.25f,
                16f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Микроблики материалов",
                () => Advanced().GlintIntensity,
                value => SaveAdvanced(settings => settings.GlintIntensity = value),
                0f,
                0.5f,
                _refreshers));
            opticsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Порог микробликов",
                () => Advanced().GlintThreshold,
                value => SaveAdvanced(settings => settings.GlintThreshold = value),
                0f,
                4f,
                _refreshers));
            atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Светящаяся пыль",
                () => Advanced().VolumetricDustIntensity,
                value => SaveAdvanced(settings => settings.VolumetricDustIntensity = value),
                0f,
                0.25f,
                _refreshers));
            atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Масштаб светящейся пыли",
                () => Advanced().VolumetricDustScale,
                value => SaveAdvanced(settings => settings.VolumetricDustScale = value),
                0.1f,
                8f,
                _refreshers));
            atmosphereGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Скорость светящейся пыли",
                () => Advanced().VolumetricDustSpeed,
                value => SaveAdvanced(settings => settings.VolumetricDustSpeed = value),
                0f,
                2f,
                _refreshers));
            displayGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Структура люминофора",
                () => Advanced().PhosphorMaskIntensity,
                value => SaveAdvanced(settings => settings.PhosphorMaskIntensity = value),
                0f,
                0.35f,
                _refreshers));
            displayGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Перцептивный dithering",
                () => Advanced().DitheringIntensity,
                value => SaveAdvanced(settings => settings.DitheringIntensity = value),
                0f,
                1f,
                _refreshers));
            temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Послесвечение люминофора",
                () => Advanced().TemporalPersistenceIntensity,
                value => SaveAdvanced(
                    settings => settings.TemporalPersistenceIntensity = value),
                0f,
                0.8f,
                _refreshers));
            temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Затухание послесвечения",
                () => Advanced().TemporalPersistenceDecay,
                value => SaveAdvanced(
                    settings => settings.TemporalPersistenceDecay = value),
                0f,
                0.98f,
                _refreshers));
            temporalGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Temporal stability света",
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
                "Масштаб UI",
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
                "Яркость окружения",
                () => GetLightingValue(static engine => engine.AmbientIntensity),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                1f,
                _refreshers));
            ambientGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет окружения",
                () => _lightingEngine.AmbientColor,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetAmbientColor(setting)),
                0f,
                4f,
                _refreshers));
            ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Мощность излучения",
                () => GetLightingValue(static engine => engine.EmissionScale),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                8f,
                _refreshers));

            dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Мощность emission игрока",
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
                "Частота расчёта dynamic emission",
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
                "Цвет источника: красный",
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
                "Цвет источника: зелёный",
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
                "Цвет источника: синий",
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
                "Поглощение в пустой среде",
                () => _lightingEngine.EmptyExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetEmptyExtinctionColor(setting)),
                0f,
                4f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Поглощение физической массой",
                () => _lightingEngine.SolidExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetSolidExtinctionColor(setting)),
                0f,
                4f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Ослабление света в пустой среде",
                () => GetLightingValue(static engine => engine.EmptyExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                2f,
                _refreshers));
            extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Ослабление света физической массой",
                () => GetLightingValue(static engine => engine.SolidExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                2f,
                _refreshers));
            bounceGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Сила непрямого диффузного света",
                () => GetLightingValue(static engine => engine.BounceStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                1f,
                _refreshers));
            aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Радиус контактного AO",
                () => GetLightingValue(static engine => engine.AmbientOcclusionRadiusCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f,
                _refreshers));
            aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Интенсивность контактного AO",
                () => GetLightingValue(static engine => engine.AmbientOcclusionStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f,
                _refreshers));
            VisualElement maximumLightMultiplierSlider = PauseMenuUIFactory.CreateBoundSlider(
                "Максимум светового множителя",
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
                "Пропускание света — диагностика",
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
                "Минимальное пропускание каскадов",
                () => GetLightingValue(static engine => engine.MinimumTransmission),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetMinimumTransmission(setting)),
                0.0001f,
                0.1f,
                _refreshers));
            boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Безопасная граница света",
                () => _lightingEngine.LightSafeBorder,
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetLightSafeBorder(setting)),
                0f,
                8f,
                _refreshers));
            Toggle finalLightingClampToggle = PauseMenuUIFactory.CreateBoundToggle(
                "Ограничивать итоговый свет",
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
                "Скорость shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerSpeedScale,
                value => SaveShaderSetting(
                    config => config.TerrainShimmerSpeedScale = value),
                0f,
                10f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerColor,
                value => SaveShaderSetting(config => config.TerrainShimmerColor = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Скорость пульсации террейна",
                () => _clientConfig.Config.TerrainPulseSpeedScale,
                value => SaveShaderSetting(config => config.TerrainPulseSpeedScale = value),
                0f,
                10f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Излучение поверхности мира",
                () => _clientConfig.Config.TransitEmissionStrength,
                value => SaveShaderSetting(config => config.TransitEmissionStrength = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет излучения поверхности",
                () => _clientConfig.Config.TransitEmissionColor,
                value => SaveShaderSetting(config => config.TransitEmissionColor = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
                "Излучение дальней поверхности",
                () => _clientConfig.Config.PerspectiveEmissionStrength,
                value => SaveShaderSetting(
                    config => config.PerspectiveEmissionStrength = value),
                0f,
                8f,
                _refreshers));
            worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
                "Цвет дальней поверхности",
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
                text = "Инструменты разработчика",
                value = false,
            };
            debugSection.AddToClassList("settings-section");
            debugSection.AddToClassList("settings-section--debug");
            _debugSection = debugSection;

            debugSection.Add(PauseMenuUIFactory.CreateLabel("Инструменты разработчика"));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Тест: Kick сервером", () =>
            {
                _connectionService.TriggerDisconnect("Тестовый дисконнект от сервера");
                _closeMenu();
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Тест: Reconnect", () =>
            {
                _connectionService.TriggerReconnect("Сервер перезагружается");
                _closeMenu();
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Тест: Открыть URL", () =>
            {
                SendElementClick("open_url_test");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Тест модального окна", () =>
            {
                SendElementClick("test_modal");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Вступить в клан", () =>
            {
                SendElementClick("join_clan");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Выйти из клана", () =>
            {
                SendElementClick("leave_clan");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Тест: Стрелка миссии", () =>
            {
                SendElementClick("test_mission_arrow");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Миссии", () =>
            {
                SendElementClick("open_missions");
            }));
            debugSection.Add(PauseMenuUIFactory.CreateButton("Стены ✗", () =>
            {
                PlayerMovementController? player = PlayerMovementController.LocalPlayer;
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
                "FinalLighting — итоговый свет",
                "Occupancy — физическая масса",
                "Albedo — альбедо",
                "Emission — излучение",
                "Transmission — пропускание",
                "DirectRadiance — прямой свет",
                "DiffuseBounce — непрямой диффузный свет",
                "ContactOcclusion — контактное затенение",
                "Exposure — экспозиция (зелёный < белой точки, красный — пересвет)",
            ];
            int activeDebugView = (int)_lightingEngine.ActiveDebugView;
            var lightingDebugView = new Button();
            void UpdateLightingDebugButton()
            {
                lightingDebugView.text =
                    $"Отладка освещения: {lightingDebugNames[activeDebugView]}";
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

            debugSection.Add(PauseMenuUIFactory.CreateLabel("Фактические параметры lighting"));
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
                text = "Обновить параметры lighting",
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
                text = "Сбросить lighting к натуральным defaults",
            };
            resetLightingPreferences.AddToClassList("pause-btn");
            debugSection.Add(resetLightingPreferences);
        }
#endif

        private static Robot? ResolveLocalRobot()
        {
            return PlayerMovementController.LocalPlayer?.GetComponent<Robot>();
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
                    ? "Оконный"
                    : "Полный экран";
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
