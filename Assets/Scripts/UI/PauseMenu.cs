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
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI.Programmator;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
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
        private TerrariaLightingEngine _lightingEngine = null!;
        [Inject]
        private PostProcessController _postProcessController = null!;
        [Inject]
        private TerrainRenderer _terrainRenderer = null!;
        [Inject]
        private GraphicsSettingsController _graphicsSettings = null!;
        private VisualElement? _menuPanel;
        private TemplateContainer? _menuTree;
        private VisualElement? _mainPage;
        private ScrollView? _mainPageScroll;
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private InputAction? _escapeAction;
        private float _originalScale;
        private bool _originalScaleCaptured;
        private Button? _fullscreenButton;
        private bool _initialized;

        private float GetConfiguredBusVolume(AudioBusType busType)
        {
            return busType switch
            {
                AudioBusType.Master => _clientConfig.Config.MasterVolume,
                AudioBusType.SFX => _clientConfig.Config.SfxVolume,
                AudioBusType.Music => _clientConfig.Config.MusicVolume,
                AudioBusType.Voice => _clientConfig.Config.VoiceVolume,
                AudioBusType.Ambience => _clientConfig.Config.AmbienceVolume,
                AudioBusType.UI => _clientConfig.Config.UiVolume,
                _ => throw new ArgumentOutOfRangeException(nameof(busType), busType, "Unsupported audio bus."),
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
            EnsureEscapeAction();
            TryInitialize();
        }

        protected void OnEnable()
        {
            EnsureEscapeAction();
        }

        protected void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && (_escapeAction == null || !_escapeAction.enabled))
            {
                ToggleMenu();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || !ServiceLocator.IsInitialized)
            {
                return;
            }

            _doc ??= ServiceLocator.Resolve<UIDocument>() ?? FindAnyObjectByType<UIDocument>(FindObjectsInactive.Include);
            if (_doc == null || _doc.rootVisualElement == null || _doc.panelSettings == null)
            {
                return;
            }

            _clientConfig ??= ServiceLocator.Resolve<IClientConfigManager>();
            _networkService ??= ServiceLocator.Resolve<INetworkService>();
            _audioSystem ??= ServiceLocator.Resolve<IAudioSystem>();
            _connectionService ??= ServiceLocator.Resolve<IConnectionService>();
            _inputBlocker ??= ServiceLocator.Resolve<IInputBlocker>();
            _lightingEngine ??= ServiceLocator.Resolve<TerrariaLightingEngine>();
            _postProcessController ??= ServiceLocator.Resolve<PostProcessController>();
            _terrainRenderer ??= ServiceLocator.Resolve<TerrainRenderer>();
            _graphicsSettings ??= ServiceLocator.Resolve<GraphicsSettingsController>();

            if (_clientConfig == null || _networkService == null ||
                _audioSystem == null || _connectionService == null || _inputBlocker == null ||
                _lightingEngine == null || _postProcessController == null || _terrainRenderer == null ||
                _graphicsSettings == null)
            {
                return;
            }

            EnsureEscapeAction();

            _originalScale = _doc.panelSettings.scale;
            _originalScaleCaptured = true;

            CreateMenu(_doc.rootVisualElement);
            HideMenu();

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

            if (_menuTree != null && _menuTree.parent != null)
            {
                _menuTree.parent.Remove(_menuTree);
            }

            if (_originalScaleCaptured && _doc != null && _doc.panelSettings != null)
            {
                if (Mathf.Abs(_doc.panelSettings.scale - _originalScale) > 0.0001f)
                {
                    _doc.panelSettings.scale = _originalScale;
                }
            }

            DisposeEscapeAction();
        }

        protected void OnDisable()
        {
            DisposeEscapeAction();
        }

        private void EnsureEscapeAction()
        {
            if (_escapeAction != null)
            {
                return;
            }

            _escapeAction = new InputAction("Escape", binding: "<Keyboard>/escape");
            _escapeAction.performed += OnEscapePerformed;
            _escapeAction.Enable();
        }

        private void DisposeEscapeAction()
        {
            if (_escapeAction == null)
            {
                return;
            }

            _escapeAction.performed -= OnEscapePerformed;
            _escapeAction.Dispose();
            _escapeAction = null;
        }

        private void OnEscapePerformed(InputAction.CallbackContext _)
        {
            ToggleMenu();
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

        private static VisualElement CreateBoundSlider(
            string labelText,
            Func<float> readValue,
            Action<float> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(minimum, maximum);
            void Refresh()
            {
                float value = readValue();
                slider.SetValueWithoutNotify(value);
                label.text = $"{labelText}: {value:F2}";
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                label.text = $"{labelText}: {evt.newValue:F2}";
                onChange(evt.newValue);
            });
            container.Add(slider);
            refreshers.Add(Refresh);
            Refresh();
            return container;
        }

        private static VisualElement CreateBoundColorControls(
            string labelText,
            Func<Color> readValue,
            Action<Color> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");
            container.Add(CreateLabel(labelText));
            container.Add(CreateBoundSlider(
                $"{labelText} R",
                () => readValue().r,
                value =>
                {
                    Color color = readValue();
                    color.r = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} G",
                () => readValue().g,
                value =>
                {
                    Color color = readValue();
                    color.g = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} B",
                () => readValue().b,
                value =>
                {
                    Color color = readValue();
                    color.b = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            return container;
        }

        private static Toggle CreateBoundToggle(
            string label,
            Func<bool> readValue,
            Action<bool> onChange,
            ICollection<Action> refreshers)
        {
            var toggle = new Toggle(label);
            void Refresh()
            {
                toggle.SetValueWithoutNotify(readValue());
            }

            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            refreshers.Add(Refresh);
            Refresh();
            return toggle;
        }

        private void CreateMenu(VisualElement root)
        {
            VisualElement? existingMenu = root.Q<VisualElement>("PauseOverlay");
            if (existingMenu != null)
            {
                VisualElement existingTree = existingMenu;
                while (existingTree.parent != null && existingTree.parent != root)
                {
                    existingTree = existingTree.parent;
                }

                if (existingTree.parent == root)
                {
                    root.Remove(existingTree);
                }
            }

            VisualTreeAsset menuTemplate = Resources.Load<VisualTreeAsset>("UI/PauseMenu") ??
                throw new InvalidOperationException(
                    "[PauseMenu] Resources/UI/PauseMenu.uxml is required.");
            TemplateContainer menuTree = menuTemplate.Instantiate();
            _menuTree = menuTree;
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
            ScrollView effectsScroll = menuTree.Q<ScrollView>("EffectsScroll") ??
                throw new InvalidOperationException("[PauseMenu] EffectsScroll is missing from PauseMenu.uxml.");
            ScrollView audioScroll = menuTree.Q<ScrollView>("AudioScroll") ??
                throw new InvalidOperationException("[PauseMenu] AudioScroll is missing from PauseMenu.uxml.");
            ScrollView interfaceScroll = menuTree.Q<ScrollView>("InterfaceScroll") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceScroll is missing from PauseMenu.uxml.");
            ScrollView advancedScroll = menuTree.Q<ScrollView>("AdvancedScroll") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedScroll is missing from PauseMenu.uxml.");
            Button settingsBack = menuTree.Q<Button>("SettingsBack") ??
                throw new InvalidOperationException("[PauseMenu] SettingsBack is missing from PauseMenu.uxml.");
            settingsBack.clicked += CloseSettings;

            Button graphicsTab = menuTree.Q<Button>("GraphicsTab") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsTab is missing from PauseMenu.uxml.");
            Button displayTab = menuTree.Q<Button>("DisplayTab") ??
                throw new InvalidOperationException("[PauseMenu] DisplayTab is missing from PauseMenu.uxml.");
            Button effectsTab = menuTree.Q<Button>("EffectsTab") ??
                throw new InvalidOperationException("[PauseMenu] EffectsTab is missing from PauseMenu.uxml.");
            Button audioTab = menuTree.Q<Button>("AudioTab") ??
                throw new InvalidOperationException("[PauseMenu] AudioTab is missing from PauseMenu.uxml.");
            Button interfaceTab = menuTree.Q<Button>("InterfaceTab") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceTab is missing from PauseMenu.uxml.");
            Button advancedTab = menuTree.Q<Button>("AdvancedTab") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedTab is missing from PauseMenu.uxml.");

            VisualElement[] settingsPages =
            [
                graphicsScroll,
                displayScroll,
                effectsScroll,
                audioScroll,
                interfaceScroll,
                advancedScroll,
            ];
            Button[] settingsTabs =
            [
                graphicsTab,
                displayTab,
                effectsTab,
                audioTab,
                interfaceTab,
                advancedTab,
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
            effectsTab.clicked += () => ShowSettingsPage(2);
            audioTab.clicked += () => ShowSettingsPage(3);
            interfaceTab.clicked += () => ShowSettingsPage(4);
            advancedTab.clicked += () => ShowSettingsPage(5);
            root.Add(menuTree);

            _mainPageScroll.Add(CreateButton("Продолжить", CloseMenu));
            _mainPageScroll.Add(CreateButton("Настройки", OpenSettings));
            _mainPageScroll.Add(CreateButton("В главное меню", ExitToMainMenu));
            _mainPageScroll.Add(CreateButton("Выйти", QuitGame));

            VisualElement displaySection = CreateSettingsSection(
                "Экран",
                "Разрешение, режим окна и частота кадров.");
            VisualElement graphicsSection = CreateSettingsSection(
                "Графика",
                "Готовые профили качества. Изменение параметров переводит профиль в «Пользовательский».");
            VisualElement postProcessSection = CreateSettingsSection(
                "Постобработка",
                "Эффекты изображения: свечение, цвет, зернистость и размытие движения.");
            VisualElement worldMaterialsSection = CreateSettingsSection(
                "Материалы мира",
                "Визуальные параметры поверхности мира и дальних областей.");
            VisualElement audioSection = CreateSettingsSection(
                "Звук",
                "Громкость отдельных звуковых шин. Отсутствующий аудиоконтент не блокирует игру.");
            VisualElement interfaceSection = CreateSettingsSection(
                "Интерфейс",
                "Масштаб и отображение элементов игрового интерфейса.");
            var advancedGraphicsSection = new Foldout
            {
                text = "Освещение",
                value = true,
            };
            advancedGraphicsSection.AddToClassList("settings-section");
            advancedGraphicsSection.AddToClassList("settings-section--advanced");

            postProcessSection.AddToClassList("settings-section--effects");
            worldMaterialsSection.AddToClassList("settings-section--advanced");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var debugSection = new Foldout
            {
                text = "Инструменты разработчика",
                value = false,
            };
            debugSection.AddToClassList("settings-section");
            debugSection.AddToClassList("settings-section--debug");

            debugSection.Add(CreateLabel("Инструменты разработчика"));
            debugSection.Add(CreateButton("Тест: Kick сервером", () =>
            {
                _connectionService.TriggerDisconnect("Тестовый дисконнект от сервера");
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Тест: Reconnect", () =>
            {
                _connectionService.TriggerReconnect("Сервер перезагружается");
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Тест: Открыть URL", () =>
            {
                _networkService.Send(new ElementClickPacket("open_url_test", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Тест модального окна", () =>
            {
                _networkService.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Вступить в клан", () =>
            {
                _networkService.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Выйти из клана", () =>
            {
                _networkService.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Тест: Стрелка миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("test_mission_arrow", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));
            debugSection.Add(CreateButton("Стены ✗", () =>
            {
                PlayerMovementController? player = PlayerMovementController.LocalPlayer;
                if (player != null)
                {
                    player.IgnoreCollision = !player.IgnoreCollision;
                    CloseMenu();
                }
            }));
#endif

            audioSection.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master));
            audioSection.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX));
            audioSection.Add(CreateAudioSlider("Музыка", AudioBusType.Music));
            audioSection.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience));
            audioSection.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice));
            audioSection.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI));

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
                0.5f,
                2f));

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

            var graphicsRefreshers = new List<Action>();
            Foldout customGraphicsSection = null!;
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

            void MarkGraphicsCustom()
            {
                _graphicsSettings.MarkCustom();
                UpdateLightingQualityButton();
            }

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
                    customGraphicsSection.value = true;
                }
                else
                {
                    _graphicsSettings.SelectStandardPreset(nextPreset);
                }

                foreach (Action refresh in graphicsRefreshers)
                {
                    refresh();
                }
            };
            lightingQuality.AddToClassList("pause-btn");
            graphicsRefreshers.Add(UpdateLightingQualityButton);
            UpdateLightingQualityButton();
            graphicsSection.Add(lightingQuality);

            customGraphicsSection = new Foldout
            {
                text = "Пользовательский профиль",
                value = _graphicsSettings.SelectedPreset == GraphicsPreset.Custom,
            };
            customGraphicsSection.AddToClassList("settings-section");
            customGraphicsSection.AddToClassList("settings-section--custom");

            var customGraphicsButton = new Button
            {
                text = "Настроить пользовательскую графику",
            };
            customGraphicsButton.AddToClassList("pause-btn");
            customGraphicsButton.clicked += () =>
            {
                _graphicsSettings.SelectCustomPreset();
                customGraphicsSection.value = true;
                foreach (Action refresh in graphicsRefreshers)
                {
                    refresh();
                }
            };
            graphicsSection.Add(customGraphicsButton);

            void ApplyCustomTechnicalSettings(
                Func<GraphicsQualitySettings, GraphicsQualitySettings> update)
            {
                _graphicsSettings.MarkCustom();
                GraphicsQualitySettings settings = update(_graphicsSettings.CustomSettings);
                _graphicsSettings.SetCustomSettings(settings);
                customGraphicsSection.value = true;
                UpdateLightingQualityButton();
            }

            customGraphicsSection.Add(CreateBoundSlider(
                "Плотность lighting",
                () => _graphicsSettings.CustomSettings.LightingMinimumPixelsPerCell,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMinimumPixelsPerCell = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                8f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Максимальный размер lighting",
                () => _graphicsSettings.CustomSettings.LightingMaximumTextureDimension,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumTextureDimension = Mathf.RoundToInt(value);
                    return settings;
                }),
                128f,
                4096f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Максимум dynamic lights",
                () => _graphicsSettings.CustomSettings.LightingMaximumLightCount,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumLightCount = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                2048f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Шаги lighting cascade",
                () => _graphicsSettings.CustomSettings.LightingMaximumRaySteps,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingMaximumRaySteps = Mathf.RoundToInt(value);
                    return settings;
                }),
                1f,
                128f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Частота lighting solve",
                () => _graphicsSettings.CustomSettings.LightingUpdatesPerSecond,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingUpdatesPerSecond = Mathf.Round(value);
                    return settings;
                }),
                1f,
                60f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Размер cascade atlas",
                () => _graphicsSettings.CustomSettings.LightingCascadeAtlasLimit,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.LightingCascadeAtlasLimit = Mathf.RoundToInt(value);
                    return settings;
                }),
                128f,
                4096f,
                graphicsRefreshers));
            customGraphicsSection.Add(CreateBoundSlider(
                "Render scale",
                () => _graphicsSettings.CustomSettings.RenderScale,
                value => ApplyCustomTechnicalSettings(settings =>
                {
                    settings.RenderScale = value;
                    return settings;
                }),
                0.5f,
                1f,
                graphicsRefreshers));

            var customVSyncButton = new Button();
            void RefreshCustomVSync()
            {
                customVSyncButton.text =
                    $"VSync: {_graphicsSettings.CustomSettings.VSyncCount}";
            }

            customVSyncButton.clicked += () => ApplyCustomTechnicalSettings(settings =>
            {
                settings.VSyncCount = (settings.VSyncCount + 1) % 5;
                return settings;
            });
            customVSyncButton.AddToClassList("pause-btn");
            graphicsRefreshers.Add(RefreshCustomVSync);
            RefreshCustomVSync();
            customGraphicsSection.Add(customVSyncButton);

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
            graphicsRefreshers.Add(RefreshCustomAntiAliasing);
            RefreshCustomAntiAliasing();
            customGraphicsSection.Add(customAntiAliasingButton);

            graphicsSection.Add(customGraphicsSection);

            Toggle ambientOcclusionToggle = CreateBoundToggle(
                "Контактное затенение (AO)",
                () => _lightingEngine.AmbientOcclusionEnabled,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetAmbientOcclusionEnabled(value);
                },
                graphicsRefreshers);
            graphicsSection.Add(ambientOcclusionToggle);

            Toggle globalIlluminationToggle = CreateBoundToggle(
                "Непрямой диффузный свет",
                () => _lightingEngine.DiffuseBounceEnabled,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetDiffuseBounceEnabled(value);
                },
                graphicsRefreshers);
            graphicsSection.Add(globalIlluminationToggle);

            void ApplyLightingSetting(
                float value,
                System.Action<TerrariaLightingEngine, float> apply)
            {
                MarkGraphicsCustom();
                apply(_lightingEngine, value);
            }

            float GetLightingValue(System.Func<TerrariaLightingEngine, float> actualValue)
            {
                return actualValue(_lightingEngine);
            }

            void ApplyLightingColor(
                Color value,
                Action<TerrariaLightingEngine, Color> apply)
            {
                MarkGraphicsCustom();
                apply(_lightingEngine, value);
            }

            advancedGraphicsSection.Add(CreateBoundSlider(
                "Яркость окружения",
                () => GetLightingValue(static engine => engine.AmbientIntensity),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                1f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundColorControls(
                "Цвет окружения",
                () => _lightingEngine.AmbientColor,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetAmbientColor(setting)),
                0f,
                4f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Мощность излучения",
                () => GetLightingValue(static engine => engine.EmissionScale),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                8f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateLabel("Динамические источники"));
            Robot? localRobot = PlayerMovementController.LocalPlayer?.GetComponent<Robot>();
            if (localRobot != null)
            {
                advancedGraphicsSection.Add(CreateBoundSlider(
                    "Мощность emission игрока",
                    () => localRobot.DynamicLightIntensity,
                    value =>
                    {
                        MarkGraphicsCustom();
                        localRobot.SetDynamicLightIntensity(value);
                    },
                    0f,
                    4f,
                    graphicsRefreshers));
                advancedGraphicsSection.Add(CreateBoundSlider(
                    "Частота расчёта dynamic emission",
                    () => _lightingEngine.DynamicLightUpdatesPerSecond,
                    value =>
                    {
                        MarkGraphicsCustom();
                        _lightingEngine.SetDynamicLightUpdatesPerSecond(value);
                    },
                    1f,
                    LightingConfigLimits.DynamicLightUpdatesPerSecond,
                    graphicsRefreshers));

                advancedGraphicsSection.Add(CreateBoundSlider(
                    "Цвет источника: красный",
                    () => localRobot.DynamicLightColor.r,
                    value =>
                    {
                        MarkGraphicsCustom();
                        Color color = localRobot.DynamicLightColor;
                        localRobot.SetDynamicLightColor(new Color(value, color.g, color.b, 1f));
                    },
                    0f,
                    1f,
                    graphicsRefreshers));
                advancedGraphicsSection.Add(CreateBoundSlider(
                    "Цвет источника: зелёный",
                    () => localRobot.DynamicLightColor.g,
                    value =>
                    {
                        MarkGraphicsCustom();
                        Color color = localRobot.DynamicLightColor;
                        localRobot.SetDynamicLightColor(new Color(color.r, value, color.b, 1f));
                    },
                    0f,
                    1f,
                    graphicsRefreshers));
                advancedGraphicsSection.Add(CreateBoundSlider(
                    "Цвет источника: синий",
                    () => localRobot.DynamicLightColor.b,
                    value =>
                    {
                        MarkGraphicsCustom();
                        Color color = localRobot.DynamicLightColor;
                        localRobot.SetDynamicLightColor(new Color(color.r, color.g, value, 1f));
                    },
                    0f,
                    1f,
                    graphicsRefreshers));
            }
            else
            {
                advancedGraphicsSection.Add(CreateLabel(
                    "Источник игрока недоступен до появления локального робота."));
            }

            advancedGraphicsSection.Add(CreateLabel("Физическое поглощение"));
            advancedGraphicsSection.Add(CreateBoundColorControls(
                "Поглощение в пустой среде",
                () => _lightingEngine.EmptyExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetEmptyExtinctionColor(setting)),
                0f,
                4f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundColorControls(
                "Поглощение физической массой",
                () => _lightingEngine.SolidExtinctionRgb,
                value => ApplyLightingColor(
                    value,
                    static (engine, setting) => engine.SetSolidExtinctionColor(setting)),
                0f,
                4f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Ослабление света в пустой среде",
                () => GetLightingValue(static engine => engine.EmptyExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                2f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Ослабление света физической массой",
                () => GetLightingValue(static engine => engine.SolidExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                2f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateLabel("Непрямой диффузный свет"));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Сила непрямого диффузного света",
                () => GetLightingValue(static engine => engine.BounceStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                1f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateLabel("Контактное затенение"));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Радиус контактного AO",
                () => GetLightingValue(static engine => engine.AmbientOcclusionRadiusCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Интенсивность контактного AO",
                () => GetLightingValue(static engine => engine.AmbientOcclusionStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateLabel("Границы расчёта"));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Максимум светового множителя",
                () => GetLightingValue(static engine => engine.MaximumLightMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Пропускание света — диагностика",
                () => GetLightingValue(static engine => engine.TransmittanceDebugDistanceCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetTransmittanceDebugDistance(setting)),
                2f,
                32f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Минимальное пропускание каскадов",
                () => GetLightingValue(static engine => engine.MinimumTransmission),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetMinimumTransmission(setting)),
                0.0001f,
                0.1f,
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
                "Безопасная граница света",
                () => _lightingEngine.LightSafeBorder,
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetLightSafeBorder(setting)),
                0f,
                8f,
                graphicsRefreshers));
            Toggle finalLightingClampToggle = CreateBoundToggle(
                "Ограничивать итоговый свет",
                () => _lightingEngine.EnableFinalLightingClamp,
                value =>
                {
                    MarkGraphicsCustom();
                    _lightingEngine.SetFinalLightingClampEnabled(value);
                },
                graphicsRefreshers);
            advancedGraphicsSection.Add(finalLightingClampToggle);

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
                    (TerrariaLightingEngine.DebugView)activeDebugView);

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
                localRobot?.ResetDynamicLightPreferences();
                foreach (Action refresh in graphicsRefreshers)
                {
                    refresh();
                }

                UpdateLightingDiagnostics();
            })
            {
                text = "Сбросить lighting к натуральным defaults",
            };
            resetLightingPreferences.AddToClassList("pause-btn");
            debugSection.Add(resetLightingPreferences);
#endif

            graphicsSection.Add(CreateLabel("Визуальные эффекты"));

            _postProcessController.EnsureVolumeSetup();
            var pp = _postProcessController;
            void SavePostProcess(Action apply)
            {
                MarkGraphicsCustom();
                apply();
                _clientConfig.Save();
            }

            graphicsSection.Add(CreateBoundSlider(
                "Свечение",
                () => _clientConfig.Config.BloomIntensity,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.BloomIntensity = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                5f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Порог свечения",
                () => _clientConfig.Config.BloomThreshold,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.BloomThreshold = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                2f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Рассеивание свечения",
                () => _clientConfig.Config.BloomScatter,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.BloomScatter = value;
                    pp.ApplyClientConfig();
                }),
                0.1f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет свечения",
                () => _clientConfig.Config.BloomTint,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.BloomTint = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Виньетка",
                () => _clientConfig.Config.VignetteIntensity,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.VignetteIntensity = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Мягкость виньетки",
                () => _clientConfig.Config.VignetteSmoothness,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.VignetteSmoothness = value;
                    pp.ApplyClientConfig();
                }),
                0.01f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Центр виньетки X",
                () => _clientConfig.Config.VignetteCenter.x,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.VignetteCenter = new Vector2(
                        value,
                        _clientConfig.Config.VignetteCenter.y);
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Центр виньетки Y",
                () => _clientConfig.Config.VignetteCenter.y,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.VignetteCenter = new Vector2(
                        _clientConfig.Config.VignetteCenter.x,
                        value);
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет виньетки",
                () => _clientConfig.Config.VignetteColor,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.VignetteColor = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Хроматическая аберрация",
                () => _clientConfig.Config.ChromaticAberrationIntensity,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ChromaticAberrationIntensity = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Экспозиция",
                () => _clientConfig.Config.ColorGradingExposure,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingExposure = value;
                    pp.ApplyClientConfig();
                }),
                -4f,
                4f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Контраст",
                () => _clientConfig.Config.ColorGradingContrast,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingContrast = value;
                    pp.ApplyClientConfig();
                }),
                -1f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Насыщенность",
                () => _clientConfig.Config.ColorGradingSaturation,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingSaturation = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                2f,
                graphicsRefreshers));
            Toggle toneMappingToggle = CreateBoundToggle(
                "Тональное отображение",
                () => _clientConfig.Config.ColorGradingToneMapping,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingToneMapping = value;
                    pp.ApplyClientConfig();
                }),
                graphicsRefreshers);
            graphicsSection.Add(toneMappingToggle);
            graphicsSection.Add(CreateBoundSlider(
                "Белая точка tone mapping",
                () => _clientConfig.Config.ColorGradingToneMappingWhitePoint,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingToneMappingWhitePoint = value;
                    pp.ApplyClientConfig();
                }),
                0.25f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цветовой фильтр",
                () => _clientConfig.Config.ColorGradingFilter,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.ColorGradingFilter = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Зернистость",
                () => _clientConfig.Config.EigengrauIntensity,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.EigengrauIntensity = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Порог тёмных областей зернистости",
                () => _clientConfig.Config.EigengrauDarknessThreshold,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.EigengrauDarknessThreshold = value;
                    pp.ApplyClientConfig();
                }),
                0.02f,
                0.75f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Масштаб зернистости",
                () => _clientConfig.Config.EigengrauNoiseScale,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.EigengrauNoiseScale = value;
                    pp.ApplyClientConfig();
                }),
                0.75f,
                2f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Скорость зернистости",
                () => _clientConfig.Config.EigengrauAnimationSpeed,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.EigengrauAnimationSpeed = value;
                    pp.ApplyClientConfig();
                }),
                1f,
                60f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет зернистости",
                () => _clientConfig.Config.EigengrauColor,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.EigengrauColor = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Размытие движения",
                () => _clientConfig.Config.MotionBlurIntensity,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.MotionBlurIntensity = value;
                    pp.ApplyClientConfig();
                }),
                0f,
                1f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Сэмплы размытия движения",
                () => _clientConfig.Config.MotionBlurMaxSamples,
                value => SavePostProcess(() =>
                {
                    _clientConfig.Config.MotionBlurMaxSamples = Mathf.Clamp(
                        Mathf.RoundToInt(value),
                        2,
                        32);
                    pp.ApplyClientConfig();
                }),
                2f,
                32f,
                graphicsRefreshers));
            void SaveShaderSetting(Action apply)
            {
                apply();
                _graphicsSettings.ApplyCustomWorldMaterialSettings();
            }

            graphicsSection.Add(CreateLabel("Материалы мира"));
            graphicsSection.Add(CreateBoundSlider(
                "Масштаб flow X",
                () => _clientConfig.Config.TerrainFlowScale.x,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainFlowScale = new Vector2(
                        value,
                        _clientConfig.Config.TerrainFlowScale.y)),
                0.001f,
                1024f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Масштаб flow Y",
                () => _clientConfig.Config.TerrainFlowScale.y,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainFlowScale = new Vector2(
                        _clientConfig.Config.TerrainFlowScale.x,
                        value)),
                0.001f,
                1024f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Скорость shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerSpeedScale,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainShimmerSpeedScale = value),
                0f,
                10f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerColor,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainShimmerColor = value),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Диагностический цвет террейна",
                () => _clientConfig.Config.TerrainDebugColor,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainDebugColor = value),
                0f,
                8f,
                graphicsRefreshers));
            Toggle terrainDebugToggle = CreateBoundToggle(
                "Диагностический режим террейна",
                () => _clientConfig.Config.TerrainDebugMode,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainDebugMode = value),
                graphicsRefreshers);
            graphicsSection.Add(terrainDebugToggle);
            graphicsSection.Add(CreateBoundSlider(
                "Скорость пульсации террейна",
                () => _clientConfig.Config.TerrainPulseSpeedScale,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TerrainPulseSpeedScale = value),
                0f,
                10f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Излучение поверхности мира",
                () => _clientConfig.Config.TransitEmissionStrength,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TransitEmissionStrength = value),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет излучения поверхности",
                () => _clientConfig.Config.TransitEmissionColor,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.TransitEmissionColor = value),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Излучение дальней поверхности",
                () => _clientConfig.Config.PerspectiveEmissionStrength,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.PerspectiveEmissionStrength = value),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundColorControls(
                "Цвет дальней поверхности",
                () => _clientConfig.Config.PerspectiveEmissionColor,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.PerspectiveEmissionColor = value),
                0f,
                8f,
                graphicsRefreshers));
            graphicsSection.Add(CreateBoundSlider(
                "Физическая плотность поверхности",
                () => _clientConfig.Config.SurfaceOccupancy,
                value => SaveShaderSetting(() =>
                    _clientConfig.Config.SurfaceOccupancy = value),
                0f,
                1f,
                graphicsRefreshers));

            MoveChildrenStartingAt(
                graphicsSection,
                "Визуальные эффекты",
                postProcessSection);
            MoveChildrenStartingAt(
                postProcessSection,
                "Материалы мира",
                worldMaterialsSection);

            displayScroll.contentContainer.Add(displaySection);
            graphicsScroll.contentContainer.Add(graphicsSection);
            effectsScroll.contentContainer.Add(postProcessSection);
            audioScroll.contentContainer.Add(audioSection);
            interfaceScroll.contentContainer.Add(interfaceSection);
            advancedScroll.contentContainer.Add(advancedGraphicsSection);
            advancedScroll.contentContainer.Add(worldMaterialsSection);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            advancedScroll.contentContainer.Add(debugSection);
#endif

            // Apply the initial page after all dynamic content has been attached.
            // ScrollView owns its content container; adding sections directly to it
            // can leave the viewport empty after a domain reload.
            ShowSettingsPage(0);

            _settingsPage.style.display = DisplayStyle.None;
        }

        private VisualElement CreateAudioSlider(string title, AudioBusType busType)
        {
            float currentVol = GetConfiguredBusVolume(busType);
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

        private static VisualElement CreateSettingsSection(string title, string description)
        {
            var section = new VisualElement();
            section.AddToClassList("settings-section");

            var heading = new Label(title);
            heading.AddToClassList("settings-section__title");
            section.Add(heading);

            var descriptionLabel = new Label(description);
            descriptionLabel.AddToClassList("settings-section__description");
            section.Add(descriptionLabel);
            return section;
        }

        private static void MoveChildrenStartingAt(
            VisualElement source,
            string markerText,
            VisualElement destination)
        {
            int markerIndex = -1;
            for (int index = 0; index < source.childCount; index++)
            {
                if (source.ElementAt(index) is Label label &&
                    string.Equals(label.text, markerText, StringComparison.Ordinal))
                {
                    markerIndex = index;
                    break;
                }
            }

            if (markerIndex < 0)
            {
                throw new InvalidOperationException(
                    $"[PauseMenu] Settings marker '{markerText}' was not built.");
            }

            while (markerIndex < source.childCount)
            {
                VisualElement child = source.ElementAt(markerIndex);
                source.RemoveAt(markerIndex);
                destination.Add(child);
            }
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
            HideMenu();
        }

        private void HideMenu()
        {
            _isOpen = false;
            IsMenuOpen = false;
            if (_menuPanel != null)
            {
                _menuPanel.style.display = DisplayStyle.None;
            }
        }

        private void SendClientConfig()
        {
            var context = new List<StringPairPacket>();

            context.Add(new StringPairPacket("master_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Master) * 255)).ToString()));
            context.Add(new StringPairPacket("sfx_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.SFX) * 255)).ToString()));
            context.Add(new StringPairPacket("music_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Music) * 255)).ToString()));
            context.Add(new StringPairPacket("ambience_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Ambience) * 255)).ToString()));
            context.Add(new StringPairPacket("voice_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Voice) * 255)).ToString()));
            context.Add(new StringPairPacket("ui_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.UI) * 255)).ToString()));

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
            ShowConfirmation(
                "Выход из игры",
                "Вы уверены, что хотите выйти?",
                "Выйти",
                () =>
                {
#if UNITY_EDITOR
                    Debug.Log("[PauseMenu] Выход из игры");
#else
                    Application.Quit();
#endif
                });
        }

        private void ExitToMainMenu()
        {
            ShowConfirmation(
                "Выйти в главное меню",
                "Вы уверены? Текущая сессия будет закрыта.",
                "В меню",
                () => BootstrapLifetimeScope.Instance?.ReturnToMainMenu());
        }

        private void ShowConfirmation(string title, string description, string confirmText, Action onConfirm)
        {
            if (_doc == null)
            {
                return;
            }

            var root = _doc.rootVisualElement;

            var overlay = new VisualElement();
            overlay.name = "ConfirmOverlay";
            overlay.AddToClassList("pause-confirm-overlay");
            overlay.AddToClassList("ui-overlay");
            overlay.AddToClassList("ui-overlay--modal");

            var panel = new VisualElement();
            panel.AddToClassList("pause-confirm-panel");
            panel.AddToClassList("ui-panel");
            panel.AddToClassList("ui-panel--modal");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("pause-confirm-title");
            panel.Add(titleLabel);

            var descLabel = new Label(description);
            descLabel.AddToClassList("pause-confirm-desc");
            panel.Add(descLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("pause-confirm-buttons");
            buttonsRow.AddToClassList("ui-actions-row");

            var confirmBtn = new Button(() =>
            {
                root.Remove(overlay);
                onConfirm();
            });
            confirmBtn.text = confirmText;
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
