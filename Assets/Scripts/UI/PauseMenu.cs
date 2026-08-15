#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio.Backend;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI.Programmator;
using Fodinae.World;
using Fodinae.World.Lighting;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class PauseMenu : MonoBehaviour
    {
        public static bool IsMenuOpen { get; private set; }

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        private VisualElement? _menuPanel;
        private VisualElement? _mainPage;
        private ScrollView? _mainPageScroll;
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private InputAction? _escapeAction;
        private float _originalScale;
        private Button? _fullscreenButton;
        private bool _initialized;

        private float GetConfiguredBusVolume(AudioBusType busType, string preferenceKey, float defaultValue)
        {
            if (_audioSystem is AudioSystem audioSystem && audioSystem.IsInitialized)
            {
                return audioSystem.GetBusVolume(busType);
            }

            return busType switch
            {
                AudioBusType.Master => _clientConfig.Config.MasterVolume,
                AudioBusType.SFX => _clientConfig.Config.SfxVolume,
                AudioBusType.Music => _clientConfig.Config.MusicVolume,
                AudioBusType.Voice => _clientConfig.Config.VoiceVolume,
                AudioBusType.Ambience => _clientConfig.Config.AmbienceVolume,
                AudioBusType.UI => _clientConfig.Config.UiVolume,
                _ => defaultValue,
            };
        }

        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IAudioSystem _audioSystem = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;

        protected void Start()
        {
            TryInitialize();
        }

        protected void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || !ServiceLocator.IsInitialized)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _doc.panelSettings == null ||
                _clientConfig == null || _projectDefaults == null || _networkService == null ||
                _audioSystem == null || _connectionService == null || _inputBlocker == null)
            {
                throw new InvalidOperationException(
                    "[PauseMenu] Required DI services and UIDocument must be initialized before building pause menu.");
            }

            _escapeAction = new InputAction("Escape", binding: "<Keyboard>/escape");
            _escapeAction.performed += _ => ToggleMenu();
            _escapeAction.Enable();

            _originalScale = _doc.panelSettings.scale;

            CreateMenu(_doc.rootVisualElement);
            CloseMenu();

            var savedScale = _clientConfig.Config.UiScale;
            if (Mathf.Abs(_doc.panelSettings.scale - savedScale) > 0.0001f)
            {
                _doc.panelSettings.scale = savedScale;
            }

            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                canvas.scaleFactor = savedScale;
            }

            _initialized = true;
        }

        protected void OnDestroy()
        {
            IsMenuOpen = false;

            if (_doc != null && _doc.panelSettings != null)
            {
                if (Mathf.Abs(_doc.panelSettings.scale - _originalScale) > 0.0001f)
                {
                    _doc.panelSettings.scale = _originalScale;
                }
            }

            _escapeAction?.Dispose();
        }

        private static VisualElement CreateSlider(string labelText, float initialValue, System.Action<float> onChange, float min, float max)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(min, max);
            slider.value = initialValue;
            void UpdateLabel(float value)
            {
                label.text = $"{labelText}: {value:F2}";
            }

            UpdateLabel(initialValue);
            slider.RegisterValueChangedCallback(evt =>
            {
                UpdateLabel(evt.newValue);
                onChange(evt.newValue);
            });
            container.Add(slider);

            return container;
        }

        private void CreateMenu(VisualElement root)
        {
            VisualTreeAsset menuTemplate = Resources.Load<VisualTreeAsset>("UI/PauseMenu") ??
                throw new InvalidOperationException(
                    "[PauseMenu] Resources/UI/PauseMenu.uxml is required.");
            TemplateContainer menuTree = menuTemplate.Instantiate();
            menuTree.AddToClassList("ui-fullscreen");
            _menuPanel = menuTree.Q<VisualElement>("PauseOverlay") ??
                throw new InvalidOperationException("[PauseMenu] PauseOverlay is missing from PauseMenu.uxml.");
            _mainPage = menuTree.Q<VisualElement>("MainPage") ??
                throw new InvalidOperationException("[PauseMenu] MainPage is missing from PauseMenu.uxml.");
            _mainPageScroll = menuTree.Q<ScrollView>("MainPageScroll") ??
                throw new InvalidOperationException("[PauseMenu] MainPageScroll is missing from PauseMenu.uxml.");
            _settingsPage = menuTree.Q<VisualElement>("SettingsPage") ??
                throw new InvalidOperationException("[PauseMenu] SettingsPage is missing from PauseMenu.uxml.");
            ScrollView graphicsScroll = menuTree.Q<ScrollView>("GraphicsScroll") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsScroll is missing from PauseMenu.uxml.");
            ScrollView displayScroll = menuTree.Q<ScrollView>("DisplayScroll") ??
                throw new InvalidOperationException("[PauseMenu] DisplayScroll is missing from PauseMenu.uxml.");
            ScrollView audioScroll = menuTree.Q<ScrollView>("AudioScroll") ??
                throw new InvalidOperationException("[PauseMenu] AudioScroll is missing from PauseMenu.uxml.");
            ScrollView interfaceScroll = menuTree.Q<ScrollView>("InterfaceScroll") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceScroll is missing from PauseMenu.uxml.");
            ScrollView debugScroll = menuTree.Q<ScrollView>("DebugScroll") ??
                throw new InvalidOperationException("[PauseMenu] DebugScroll is missing from PauseMenu.uxml.");
            Button settingsBack = menuTree.Q<Button>("SettingsBack") ??
                throw new InvalidOperationException("[PauseMenu] SettingsBack is missing from PauseMenu.uxml.");
            settingsBack.clicked += CloseSettings;

            Button graphicsTab = menuTree.Q<Button>("GraphicsTab") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsTab is missing from PauseMenu.uxml.");
            Button displayTab = menuTree.Q<Button>("DisplayTab") ??
                throw new InvalidOperationException("[PauseMenu] DisplayTab is missing from PauseMenu.uxml.");
            Button audioTab = menuTree.Q<Button>("AudioTab") ??
                throw new InvalidOperationException("[PauseMenu] AudioTab is missing from PauseMenu.uxml.");
            Button interfaceTab = menuTree.Q<Button>("InterfaceTab") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceTab is missing from PauseMenu.uxml.");
            Button debugTab = menuTree.Q<Button>("DebugTab") ??
                throw new InvalidOperationException("[PauseMenu] DebugTab is missing from PauseMenu.uxml.");

            VisualElement[] settingsPages =
            [
                graphicsScroll,
                displayScroll,
                audioScroll,
                interfaceScroll,
                debugScroll,
            ];
            Button[] settingsTabs =
            [
                graphicsTab,
                displayTab,
                audioTab,
                interfaceTab,
                debugTab,
            ];
            void ShowSettingsPage(int index)
            {
                for (int i = 0; i < settingsPages.Length; i++)
                {
                    settingsPages[i].style.display = i == index
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    settingsTabs[i].EnableInClassList("settings-tab--active", i == index);
                }
            }

            graphicsTab.clicked += () => ShowSettingsPage(0);
            displayTab.clicked += () => ShowSettingsPage(1);
            audioTab.clicked += () => ShowSettingsPage(2);
            interfaceTab.clicked += () => ShowSettingsPage(3);
            debugTab.clicked += () => ShowSettingsPage(4);
            ShowSettingsPage(0);

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            debugTab.style.display = DisplayStyle.None;
#endif
            root.Add(menuTree);

            _menuPanel.pickingMode = PickingMode.Position;
            _mainPage.pickingMode = PickingMode.Position;
            _settingsPage.pickingMode = PickingMode.Position;
            graphicsScroll.pickingMode = PickingMode.Position;
            displayScroll.pickingMode = PickingMode.Position;
            audioScroll.pickingMode = PickingMode.Position;
            interfaceScroll.pickingMode = PickingMode.Position;
            debugScroll.pickingMode = PickingMode.Position;

            _mainPageScroll.Add(CreateButton("Продолжить", CloseMenu));
            _mainPageScroll.Add(CreateButton("Настройки", OpenSettings));
            _mainPageScroll.Add(CreateButton("Выйти", QuitGame));

            var debugDivider = new Label("═════ Отладка ═════");
            debugDivider.AddToClassList("pause-debug-divider");
            _mainPageScroll.Add(debugDivider);

            _mainPageScroll.Add(CreateButton("Тест: Kick сервером", () =>
            {
                _connectionService.TriggerDisconnect("Тестовый дисконнект от сервера");
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Тест: Reconnect", () =>
            {
                _connectionService.TriggerReconnect("Сервер перезагружается");
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Тест: Открыть URL", () =>
            {
                _networkService.Send(new ElementClickPacket("open_url_test", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Тест модального окна", () =>
            {
                _networkService.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Вступить в клан", () =>
            {
                _networkService.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Выйти из клана", () =>
            {
                _networkService.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Тест: Стрелка миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("test_mission_arrow", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPageScroll.Add(CreateButton("Стены ✗", () =>
            {
                var player = PlayerMovementController.LocalPlayer;
                if (player != null)
                {
                    player.IgnoreCollision = !player.IgnoreCollision;
                    CloseMenu();
                }
            }));

            VisualElement displaySection = CreateSettingsSection("Экран");
            VisualElement graphicsSection = CreateSettingsSection("Графика");
            VisualElement audioSection = CreateSettingsSection("Звук");
            VisualElement interfaceSection = CreateSettingsSection("Интерфейс");
            var advancedGraphicsSection = new Foldout
            {
                text = "Расширенные настройки графики",
                value = false,
            };
            advancedGraphicsSection.AddToClassList("settings-section");
            advancedGraphicsSection.AddToClassList("settings-section--advanced");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var debugSection = new Foldout
            {
                text = "Debug",
                value = false,
            };
            debugSection.AddToClassList("settings-section");
            debugSection.AddToClassList("settings-section--debug");
#endif

            audioSection.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master, "Audio_Master", 1f));
            audioSection.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX, "Audio_SFX", 1f));
            audioSection.Add(CreateAudioSlider("Музыка", AudioBusType.Music, "Audio_Music", 0.5f));
            audioSection.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience, "Audio_Ambience", 0.7f));
            audioSection.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice, "Audio_Voice", 1f));
            audioSection.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI, "Audio_UI", 1f));

            interfaceSection.Add(CreateSlider(
                "Масштаб UI",
                _clientConfig.Config.UiScale,
                v =>
                {
                    _clientConfig.Config.UiScale = v;
                    _clientConfig.Save();
                    if (_doc != null && _doc.panelSettings != null)
                    {
                        _doc.panelSettings.scale = v;
                    }

                    foreach (var canvas in FindObjectsByType<Canvas>())
                    {
                        canvas.scaleFactor = v;
                    }
                },
                0.65f,
                1f));

            _fullscreenButton = new Button(ToggleFullscreen);
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
            _fullscreenButton.AddToClassList("pause-btn");
            displaySection.Add(_fullscreenButton);

            var resolutions = Screen.resolutions;
            var uniqueResolutions = new System.Collections.Generic.List<Resolution>();
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var res in resolutions)
            {
                var key = $"{res.width}x{res.height}";
                if (seen.Add(key))
                {
                    uniqueResolutions.Add(res);
                }
            }

            int currentResIndex = 0;
            for (int i = 0; i < uniqueResolutions.Count; i++)
            {
                if (uniqueResolutions[i].width == Screen.currentResolution.width &&
                    uniqueResolutions[i].height == Screen.currentResolution.height)
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
                    : $"Разрешение: {uniqueResolutions[currentResIndex].width} x " +
                      uniqueResolutions[currentResIndex].height;
            }

            resolutionButton.clicked += () =>
            {
                if (uniqueResolutions.Count > 0)
                {
                    currentResIndex = (currentResIndex + 1) % uniqueResolutions.Count;
                    var resolution = uniqueResolutions[currentResIndex];
                    Screen.SetResolution(
                        resolution.width,
                        resolution.height,
                        Screen.fullScreen);
                    Debug.Log(
                        $"[PauseMenu] Resolution: {resolution.width}x{resolution.height}");
                    UpdateResolutionButton();
                }
            };
            resolutionButton.SetEnabled(uniqueResolutions.Count > 0);
            resolutionButton.AddToClassList("pause-btn");
            UpdateResolutionButton();
            displaySection.Add(resolutionButton);

            string[] lightingQualityNames =
            [
                "Низкое",
                "Среднее",
                "Высокое",
                "Ультра",
            ];
            int savedQuality = Mathf.Clamp(
                _clientConfig.Config.GraphicsQuality,
                0,
                lightingQualityNames.Length - 1);
            var lightingQuality = new Button();
            void UpdateLightingQualityButton()
            {
                lightingQuality.text =
                    $"Общее качество графики: {lightingQualityNames[savedQuality]}";
            }

            lightingQuality.clicked += () =>
            {
                savedQuality = (savedQuality + 1) % lightingQualityNames.Length;
                var engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine == null)
                {
                    Debug.LogWarning("[PauseMenu] Graphics quality selected before lighting engine initialization");
                    return;
                }

                var quality = (TerrariaLightingEngine.QualityPreset)savedQuality;
                engine.SetQuality(quality);
                _clientConfig.Config.GraphicsQuality = savedQuality;
                _clientConfig.Save();
                UpdateLightingQualityButton();
            };
            lightingQuality.AddToClassList("pause-btn");
            UpdateLightingQualityButton();
            graphicsSection.Add(lightingQuality);

            var ambientOcclusionToggle = new Toggle("Контактное затенение (AO)")
            {
                value = (TerrariaLightingEngine.Instance ??
                    FindAnyObjectByType<TerrariaLightingEngine>())?.AmbientOcclusionEnabled ??
                    _projectDefaults.Lighting.AmbientOcclusionEnabled,
            };
            ambientOcclusionToggle.RegisterValueChangedCallback(evt =>
            {
                var engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine != null)
                {
                    engine.SetAmbientOcclusionEnabled(evt.newValue);
                    return;
                }

                throw new InvalidOperationException("Lighting engine is required before changing AO.");
            });
            graphicsSection.Add(ambientOcclusionToggle);

            var globalIlluminationToggle = new Toggle("Непрямой диффузный свет")
            {
                value = (TerrariaLightingEngine.Instance ??
                    FindAnyObjectByType<TerrariaLightingEngine>())?.DiffuseBounceEnabled ??
                    _projectDefaults.Lighting.DiffuseBounceEnabled,
            };
            globalIlluminationToggle.RegisterValueChangedCallback(evt =>
            {
                var engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine != null)
                {
                    engine.SetDiffuseBounceEnabled(evt.newValue);
                    return;
                }

                throw new InvalidOperationException("Lighting engine is required before changing diffuse bounce.");
            });
            graphicsSection.Add(globalIlluminationToggle);

            void ApplyLightingSetting(
                float value,
                System.Action<TerrariaLightingEngine, float> apply)
            {
                TerrariaLightingEngine? engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine != null)
                {
                    apply(engine, value);
                    return;
                }

                throw new InvalidOperationException("Lighting engine is required before changing lighting settings.");
            }

            float GetLightingValue(
                float defaultValue,
                float minimum,
                float maximum,
                System.Func<TerrariaLightingEngine, float> actualValue)
            {
                TerrariaLightingEngine? engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                return engine != null
                    ? actualValue(engine)
                    : Mathf.Clamp(defaultValue, minimum, maximum);
            }

            graphicsSection.Add(CreateLabel("Освещение"));
            advancedGraphicsSection.Add(CreateSlider(
                "Яркость окружения",
                GetLightingValue(
                    _projectDefaults.Lighting.AmbientIntensity,
                    0f,
                    1f,
                    static engine => engine.AmbientIntensity),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                1f));
            advancedGraphicsSection.Add(CreateSlider(
                "Мощность излучения",
                GetLightingValue(
                    _projectDefaults.Lighting.EmissionScale,
                    0.1f,
                    8f,
                    static engine => engine.EmissionScale),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                8f));
            advancedGraphicsSection.Add(CreateLabel("Динамические источники"));
            Robot? localRobot = PlayerMovementController.LocalPlayer?.GetComponent<Robot>();
            Robot? GetLocalRobot() => PlayerMovementController.LocalPlayer?.GetComponent<Robot>() ?? localRobot;
            float dynamicLightIntensity = localRobot?.DynamicLightIntensity ??
                _projectDefaults.Lighting.DynamicLightIntensity;
            Color dynamicLightColor = localRobot?.DynamicLightColor ??
                _projectDefaults.Lighting.DynamicLightColor;
            advancedGraphicsSection.Add(CreateSlider(
                "Мощность emission игрока",
                dynamicLightIntensity,
                value => GetLocalRobot()?.SetDynamicLightIntensity(value),
                0f,
                4f));
            TerrariaLightingEngine? dynamicLightingEngine = TerrariaLightingEngine.Instance
                ?? FindAnyObjectByType<TerrariaLightingEngine>();
            float dynamicLightUpdatesPerSecond = dynamicLightingEngine != null
                ? dynamicLightingEngine.DynamicLightUpdatesPerSecond
                : _projectDefaults.Lighting.DynamicLightUpdatesPerSecond;
            advancedGraphicsSection.Add(CreateSlider(
                "Частота расчёта dynamic emission",
                dynamicLightUpdatesPerSecond,
                value => dynamicLightingEngine?.SetDynamicLightUpdatesPerSecond(value),
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond));

            System.Action<float> setDynamicLightRed = value =>
            {
                Robot? robot = GetLocalRobot();
                if (robot != null)
                {
                    Color color = robot.DynamicLightColor;
                    robot.SetDynamicLightColor(new Color(value, color.g, color.b, 1f));
                }
            };
            advancedGraphicsSection.Add(CreateSlider(
                "Цвет источника: красный",
                dynamicLightColor.r,
                setDynamicLightRed,
                0f,
                1f));

            System.Action<float> setDynamicLightGreen = value =>
            {
                Robot? robot = GetLocalRobot();
                if (robot != null)
                {
                    Color color = robot.DynamicLightColor;
                    robot.SetDynamicLightColor(new Color(color.r, value, color.b, 1f));
                }
            };
            advancedGraphicsSection.Add(CreateSlider(
                "Цвет источника: зелёный",
                dynamicLightColor.g,
                setDynamicLightGreen,
                0f,
                1f));

            System.Action<float> setDynamicLightBlue = value =>
            {
                Robot? robot = GetLocalRobot();
                if (robot != null)
                {
                    Color color = robot.DynamicLightColor;
                    robot.SetDynamicLightColor(new Color(color.r, color.g, value, 1f));
                }
            };
            advancedGraphicsSection.Add(CreateSlider(
                "Цвет источника: синий",
                dynamicLightColor.b,
                setDynamicLightBlue,
                0f,
                1f));

            advancedGraphicsSection.Add(CreateLabel("Физическое поглощение"));
            advancedGraphicsSection.Add(CreateSlider(
                "Ослабление света в пустой среде",
                GetLightingValue(
                    _projectDefaults.Lighting.EmptyExtinctionMultiplier,
                    0f,
                    2f,
                    static engine => engine.EmptyExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                2f));
            advancedGraphicsSection.Add(CreateSlider(
                "Ослабление света физической массой",
                GetLightingValue(
                    _projectDefaults.Lighting.SolidExtinctionMultiplier,
                    0.25f,
                    2f,
                    static engine => engine.SolidExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                2f));
            advancedGraphicsSection.Add(CreateLabel("Непрямой диффузный свет"));
            advancedGraphicsSection.Add(CreateSlider(
                "Сила непрямого диффузного света",
                GetLightingValue(
                    _projectDefaults.Lighting.BounceStrength,
                    0f,
                    1f,
                    static engine => engine.BounceStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                1f));
            advancedGraphicsSection.Add(CreateLabel("Контактное затенение"));
            advancedGraphicsSection.Add(CreateSlider(
                "Радиус контактного AO",
                GetLightingValue(
                    _projectDefaults.Lighting.AmbientOcclusionRadiusCells,
                    0.5f,
                    8f,
                    static engine => engine.AmbientOcclusionRadiusCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f));
            advancedGraphicsSection.Add(CreateSlider(
                "Интенсивность контактного AO",
                GetLightingValue(
                    _projectDefaults.Lighting.AmbientOcclusionStrength,
                    0.1f,
                    8f,
                    static engine => engine.AmbientOcclusionStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f));
            advancedGraphicsSection.Add(CreateLabel("Границы расчёта"));
            advancedGraphicsSection.Add(CreateSlider(
                "Максимум светового множителя",
                GetLightingValue(
                    _projectDefaults.Lighting.MaximumLightMultiplier,
                    0.25f,
                    LightingConfigLimits.MaximumLightMultiplier,
                    static engine => engine.MaximumLightMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier));
            advancedGraphicsSection.Add(CreateSlider(
                "Пропускание света — диагностика",
                GetLightingValue(
                    _projectDefaults.Lighting.TransmittanceDebugDistanceCells,
                    2f,
                    32f,
                    static engine => engine.TransmittanceDebugDistanceCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetTransmittanceDebugDistance(setting)),
                2f,
                32f));
            advancedGraphicsSection.Add(CreateSlider(
                "Минимальное пропускание каскадов",
                GetLightingValue(
                    _projectDefaults.Lighting.MinimumTransmission,
                    0.0001f,
                    0.1f,
                    static engine => engine.MinimumTransmission),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetMinimumTransmission(setting)),
                0.0001f,
                0.1f));
            TerrariaLightingEngine? currentLightingEngine = TerrariaLightingEngine.Instance
                ?? FindAnyObjectByType<TerrariaLightingEngine>();
            float currentLightSafeBorder = currentLightingEngine?.LightSafeBorder ??
                _projectDefaults.Lighting.LightSafeBorder;
            advancedGraphicsSection.Add(CreateSlider(
                "Безопасная граница света",
                currentLightSafeBorder,
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetLightSafeBorder(setting)),
                0f,
                8f));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
            ];
            int activeDebugView = (int)(TerrariaLightingEngine.Instance?.ActiveDebugView ??
                TerrariaLightingEngine.DebugView.FinalLighting);
            var lightingDebugView = new Button();
            void UpdateLightingDebugButton()
            {
                lightingDebugView.text =
                    $"Отладка освещения: {lightingDebugNames[activeDebugView]}";
            }

            lightingDebugView.clicked += () =>
            {
                activeDebugView = (activeDebugView + 1) % lightingDebugNames.Length;
                var engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine != null)
                {
                    engine.SetDebugView(
                        (TerrariaLightingEngine.DebugView)activeDebugView);
                }

                UpdateLightingDebugButton();
            };
            lightingDebugView.AddToClassList("pause-btn");
            UpdateLightingDebugButton();
            debugSection.Add(lightingDebugView);

            debugSection.Add(CreateLabel("Фактические параметры lighting"));
            var lightingDiagnostics = new Label();
            lightingDiagnostics.AddToClassList("pause-slider-label");
            void UpdateLightingDiagnostics()
            {
                TerrariaLightingEngine? engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine == null)
                {
                    lightingDiagnostics.text = "Lighting engine: отсутствует";
                    return;
                }

                lightingDiagnostics.text =
                    $"Quality={engine.Quality}\n" +
                    $"Config={engine.RuntimeConfigFilePath}\n" +
                    $"Debug={engine.ActiveDebugView}\n" +
                    $"AO={(engine.AmbientOcclusionEnabled ? 1 : 0)} " +
                    $"radius={engine.AmbientOcclusionRadiusCells:F2} " +
                    $"strength={engine.AmbientOcclusionStrength:F2}\n" +
                    $"DiffuseBounce={(engine.DiffuseBounceEnabled ? 1 : 0)} " +
                    $"strength={engine.BounceStrength:F3}\n" +
                    $"Ambient={engine.AmbientIntensity:F3} " +
                    $"Emission={engine.EmissionScale:F3} " +
                    $"DynamicRate={engine.DynamicLightUpdatesPerSecond:F1}\n" +
                    $"EmptyExtinction={engine.EmptyExtinctionMultiplier:F3} " +
                    $"SolidExtinction={engine.SolidExtinctionMultiplier:F3}\n" +
                    $"MinimumTransmission={engine.MinimumTransmission:F4} " +
                    $"MaximumLight={engine.MaximumLightMultiplier:F3}\n" +
                    $"SafeBorder={engine.LightSafeBorder} " +
                    $"TransmissionDistance={engine.TransmittanceDebugDistanceCells:F2}\n" +
                    $"Field={engine.FieldWidth}x{engine.FieldHeight} " +
                    $"AtlasEntries={engine.AtlasEntryCount} " +
                    $"DynamicLights={engine.DynamicLightCount} " +
                    $"Uploaded={engine.UploadedDynamicLightCount} " +
                    $"Dropped={engine.DroppedDynamicLightCount} " +
                    $"DroppedIds=[{string.Join(",", engine.DroppedDynamicLightIds)}]\n" +
                    $"ComputeAmbient={engine.ComputeAmbientColor} " +
                    $"ComputeEmptyExtinction={engine.ComputeEmptyExtinction} " +
                    $"ComputeSolidExtinction={engine.ComputeSolidExtinction}\n" +
                    $"RequiredPadding={engine.RequiredTerrainPadding} " +
                    $"SolveCount={engine.SolveCount} " +
                    $"ContactAOSolveCount={engine.ContactOcclusionSolveCount}";
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
                TerrariaLightingEngine? engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                engine?.ResetRuntimeLightingPreferences();
                GetLocalRobot()?.ResetDynamicLightPreferences();
                UpdateLightingDiagnostics();
            })
            {
                text = "Сбросить lighting к натуральным defaults",
            };
            resetLightingPreferences.AddToClassList("pause-btn");
            debugSection.Add(resetLightingPreferences);
#endif

            graphicsSection.Add(CreateLabel("Визуальные эффекты"));

            if (PostProcessController.Instance != null)
            {
                var pp = PostProcessController.Instance;
                graphicsSection.Add(CreateSlider("Свечение", pp.BloomIntensity, v => pp.BloomIntensity = v, 0f, 5f));
                graphicsSection.Add(CreateSlider("Виньетка", pp.VignetteIntensity, v => pp.VignetteIntensity = v, 0f, 1f));
                graphicsSection.Add(CreateSlider("Хроматическая аберрация", pp.ChromaticAberrationIntensity, v => pp.ChromaticAberrationIntensity = v, 0f, 1f));
                graphicsSection.Add(CreateSlider("Зернистость", pp.EigengrauIntensity, v => pp.EigengrauIntensity = v, 0f, 1f));
                graphicsSection.Add(CreateSlider("Размытие движения", pp.MotionBlurIntensity, v => pp.MotionBlurIntensity = v, 0f, 1f));
            }

            displayScroll.Add(displaySection);
            graphicsScroll.Add(graphicsSection);
            graphicsScroll.Add(advancedGraphicsSection);
            audioScroll.Add(audioSection);
            interfaceScroll.Add(interfaceSection);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugScroll.Add(debugSection);
#endif

            _settingsPage.style.display = DisplayStyle.None;
        }

        private VisualElement CreateAudioSlider(string title, AudioBusType busType, string prefKey, float defaultValue)
        {
            float currentVol = GetConfiguredBusVolume(busType, prefKey, defaultValue);
            return CreateSlider(
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

        private void SetBusVolumeInConfig(AudioBusType busType, float volume)
        {
            var config = _clientConfig.Config;
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
                    config.UiVolume = volume;
                    break;
            }

            _clientConfig.Save();
        }

        private Button CreateButton(string text, System.Action action)
        {
            var btn = new Button(action);
            btn.text = text;
            btn.AddToClassList("pause-btn");
            return btn;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("pause-slider-label");
            return label;
        }

        private static VisualElement CreateSettingsSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("settings-section");

            var heading = new Label(title);
            heading.AddToClassList("settings-section__title");
            section.Add(heading);
            return section;
        }

        private void ToggleMenu()
        {
            if (!enabled)
            {
                return;
            }

            if (ProgrammatorGrid.IsOpen)
            {
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked && !_isOpen)
            {
                var topTag = _inputBlocker.TopWindowTag;
                if (topTag != null)
                {
                    _networkService.Send(new ElementClickPacket(topTag, 0, System.Array.Empty<StringPairPacket>()));
                    return;
                }
            }

            if (_settingsPage != null && _settingsPage.style.display == DisplayStyle.Flex)
            {
                CloseSettings();
                return;
            }

            if (_isOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            Debug.Log($"[PauseMenu] Fullscreen: {Screen.fullScreen}");
            if (_fullscreenButton != null)
            {
                _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
            }
        }

        private void OpenMenu()
        {
            _isOpen = true;
            IsMenuOpen = true;
            if (_menuPanel != null)
            {
                _menuPanel.BringToFront();
                _menuPanel.pickingMode = PickingMode.Position;
                _menuPanel.style.display = DisplayStyle.Flex;
            }

            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.Flex;
            }

            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.None;
            }
        }

        private void CloseMenu()
        {
            SendClientConfig();
            _isOpen = false;
            IsMenuOpen = false;
            if (_menuPanel != null)
            {
                _menuPanel.style.display = DisplayStyle.None;
                _menuPanel.pickingMode = PickingMode.Ignore;
            }
        }

        private void SendClientConfig()
        {
            var context = new List<StringPairPacket>();

            context.Add(new StringPairPacket("master_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Master, "Audio_Master", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("sfx_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.SFX, "Audio_SFX", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("music_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Music, "Audio_Music", 0.5f) * 255)).ToString()));
            context.Add(new StringPairPacket("ambience_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Ambience, "Audio_Ambience", 0.7f) * 255)).ToString()));
            context.Add(new StringPairPacket("voice_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Voice, "Audio_Voice", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("ui_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.UI, "Audio_UI", 1f) * 255)).ToString()));

            context.Add(new StringPairPacket("ui_scale", _clientConfig.Config.UiScale.ToString("F2")));

            Debug.Log($"[PauseMenu] Sending save_client_config with {context.Count} entries");
            _networkService.Send(new ElementClickPacket("save_client_config", 0, context));
        }

        private void OpenSettings()
        {
            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.None;
            }

            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.Flex;
            }
        }

        private void CloseSettings()
        {
            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.None;
            }

            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.Flex;
            }
        }

        private void QuitGame()
        {
            ShowQuitConfirmation();
        }

        private void ShowQuitConfirmation()
        {
            if (_doc == null)
            {
                return;
            }

            var root = _doc.rootVisualElement;

            var overlay = new VisualElement();
            overlay.name = "QuitConfirmOverlay";
            overlay.AddToClassList("pause-confirm-overlay");
            overlay.AddToClassList("ui-overlay");
            overlay.AddToClassList("ui-overlay--modal");

            var panel = new VisualElement();
            panel.AddToClassList("pause-confirm-panel");
            panel.AddToClassList("ui-panel");
            panel.AddToClassList("ui-panel--modal");

            var titleLabel = new Label("Выход из игры");
            titleLabel.AddToClassList("pause-confirm-title");
            panel.Add(titleLabel);

            var descLabel = new Label("Вы уверены, что хотите выйти?");
            descLabel.AddToClassList("pause-confirm-desc");
            panel.Add(descLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("pause-confirm-buttons");
            buttonsRow.AddToClassList("ui-actions-row");

            var confirmBtn = new Button(() =>
            {
                root.Remove(overlay);
#if UNITY_EDITOR
                Debug.Log("[PauseMenu] Выход из игры");
#else
                Application.Quit();
#endif
            });
            confirmBtn.text = "Выйти";
            confirmBtn.AddToClassList("pause-btn-confirm");

            var cancelBtn = new Button(() => root.Remove(overlay));
            cancelBtn.text = "Отмена";
            cancelBtn.AddToClassList("pause-btn");

            buttonsRow.Add(confirmBtn);
            buttonsRow.Add(cancelBtn);
            panel.Add(buttonsRow);

            overlay.Add(panel);
            root.Add(overlay);
        }
    }
}
