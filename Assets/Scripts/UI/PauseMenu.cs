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
using Fodinae.World.Lighting.Quality;
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
        [Inject]
        private DisplayManager _displayManager = null!;
        [Inject]
        private IObjectResolver _resolver = null!;
        [Inject]
        private IMainMenuNavigation _mainMenuNavigation = null!;
        private VisualElement? _menuPanel;
        private TemplateContainer? _menuTree;
        private VisualElement? _mainPage;
        private ScrollView? _mainPageScroll;
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private float _originalScale;
        private bool _originalScaleCaptured;
        private Button? _fullscreenButton;
        private readonly List<Action> _settingsRefreshers = [];
        private bool _initialized;
        private bool _initializationFailed;

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
            TryInitialize();
        }

        protected void Update()
        {
            if (!_initialized && !_initializationFailed)
            {
                TryInitialize();
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ToggleMenu();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || _initializationFailed || _resolver == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _doc.panelSettings == null)
            {
                return;
            }

            _clientConfig ??= _resolver.Resolve<IClientConfigManager>();
            _networkService ??= _resolver.Resolve<INetworkService>();
            _audioSystem ??= _resolver.Resolve<IAudioSystem>();
            _connectionService ??= _resolver.Resolve<IConnectionService>();
            _inputBlocker ??= _resolver.Resolve<IInputBlocker>();
            _lightingEngine ??= _resolver.Resolve<TerrariaLightingEngine>();
            _postProcessController ??= _resolver.Resolve<PostProcessController>();
            _terrainRenderer ??= _resolver.Resolve<TerrainRenderer>();
            _graphicsSettings ??= _resolver.Resolve<GraphicsSettingsController>();
            _displayManager ??= _resolver.Resolve<DisplayManager>();

            if (_clientConfig == null || _clientConfig.Config == null || _networkService == null ||
                _audioSystem == null || _connectionService == null || _inputBlocker == null ||
                _lightingEngine == null || _postProcessController == null || _terrainRenderer == null ||
                _graphicsSettings == null || _displayManager == null)
            {
                return;
            }

            if (!_lightingEngine.IsInitialized)
            {
                // Меню строится на геттерах освещения (AmbientIntensity, EmissionScale и т.д.),
                // которые читают _runtimeConfig, создаваемый только в EnsureInitialized().
                // Порядок Start не гарантирован — ждём готовности движка; TryInitialize
                // ретраится из Update каждый кадр.
                return;
            }

            _originalScale = _doc.panelSettings.scale;
            _originalScaleCaptured = true;

            try
            {
                CreateMenu(_doc.rootVisualElement);
            }
            catch (InvalidOperationException exception)
            {
                // Обязательный контент меню (UXML/элементы) отсутствует или битый.
                // Не бросаем из Update-ретрая: это зациклило бы исключения каждый кадр.
                // Логируем один раз и помечаем как неинициализируемое — меню просто
                // не откроется, игра продолжит работать.
                Debug.LogError($"[PauseMenu] Cannot build menu: {exception.Message}");
                _initializationFailed = true;
                return;
            }

            HideMenu();

            var savedScale = _clientConfig.Config.UiScale;
            if (Mathf.Abs(_doc.panelSettings.scale - savedScale) > 0.0001f)
            {
                _doc.panelSettings.scale = savedScale;
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
        }

        private static VisualElement CreateSlider(string labelText, float initialValue, System.Action<float> onChange, float min, float max)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(min, max);
            slider.SetValueWithoutNotify(initialValue);
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
            menuTree.pickingMode = PickingMode.Ignore;
            menuTree.style.display = DisplayStyle.None;
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

            _settingsRefreshers.Clear();
            ICollection<Action> graphicsRefreshers = _settingsRefreshers;
            audioSection.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master));
            audioSection.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX));
            audioSection.Add(CreateAudioSlider("Музыка", AudioBusType.Music));
            audioSection.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience));
            audioSection.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice));
            audioSection.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI));
            Toggle muteInBackgroundToggle = CreateBoundToggle(
                "Глушить звук в фоне",
                () => _clientConfig.Config.MuteAudioInBackground,
                value => _clientConfig.UpdateAndSave(
                    config => config.MuteAudioInBackground = value),
                graphicsRefreshers);
            audioSection.Add(muteInBackgroundToggle);

            interfaceSection.Add(CreateSlider(
                "Масштаб UI",
                _clientConfig.Config.UiScale,
                v =>
                {
                    _clientConfig.UpdateAndSave(config => config.UiScale = v);
                    if (_doc != null && _doc.panelSettings != null)
                    {
                        _doc.panelSettings.scale = v;
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
                    Debug.Log(
                        $"[PauseMenu] Resolution: {resolution.width}x{resolution.height}");
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
            // remark on TerrariaLightingEngine.ApplyUnityRenderingSettings:
            // VSync is DisplayManager's alone, to avoid two owners fighting
            // over QualitySettings.vSyncCount). That button compiled, looked
            // like a working control, and did nothing when clicked - this is
            // the real one, wired to the config field DisplayManager actually
            // reads.
            Toggle vSyncToggle = CreateBoundToggle(
                "Вертикальная синхронизация",
                () => _clientConfig.Config.VSync,
                value => _displayManager.SetVSync(value),
                graphicsRefreshers);
            displaySection.Add(vSyncToggle);

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
            graphicsRefreshers.Add(UpdateLightingQualityTierButton);
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
            graphicsRefreshers.Add(UpdatePostProcessTierButton);
            UpdatePostProcessTierButton();
            graphicsSection.Add(postProcessTierButton);

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
                GraphicsQualitySettings.MinimumLightingTextureDimension,
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
            Robot? ResolveLocalRobot()
            {
                return PlayerMovementController.LocalPlayer?.GetComponent<Robot>();
            }

            advancedGraphicsSection.Add(CreateBoundSlider(
                "Мощность emission игрока",
                () => ResolveLocalRobot()?.DynamicLightIntensity ?? 0f,
                value =>
                {
                    MarkGraphicsCustom();
                    ResolveLocalRobot()?.SetDynamicLightIntensity(value);
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
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
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
                graphicsRefreshers));
            advancedGraphicsSection.Add(CreateBoundSlider(
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
                graphicsRefreshers));

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
            VisualElement maximumLightMultiplierSlider = CreateBoundSlider(
                "Максимум светового множителя",
                () => GetLightingValue(static engine => engine.MaximumLightMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier,
                graphicsRefreshers);
            void RefreshMaximumLightMultiplierState()
            {
                maximumLightMultiplierSlider.SetEnabled(_lightingEngine.EnableFinalLightingClamp);
            }

            graphicsRefreshers.Add(RefreshMaximumLightMultiplierState);
            RefreshMaximumLightMultiplierState();
            advancedGraphicsSection.Add(maximumLightMultiplierSlider);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif
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
                    RefreshMaximumLightMultiplierState();
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
                ResolveLocalRobot()?.ResetDynamicLightPreferences();
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

            _postProcessController.EnsureVolumeSetup();
            void SavePostProcess(Action<ClientConfig> update)
            {
                _graphicsSettings.UpdatePostProcessSettings(update);
            }

            void AddPostProcessGroup(string title)
            {
                var label = new Label(title);
                label.AddToClassList("pause-subsection-title");
                postProcessSection.Add(label);
            }

            AddPostProcessGroup("Свечение");
            postProcessSection.Add(CreateBoundSlider(
                "Свечение",
                () => _clientConfig.Config.BloomIntensity,
                value => SavePostProcess(config => config.BloomIntensity = value),
                0f,
                2f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Порог свечения",
                () => _clientConfig.Config.BloomThreshold,
                value => SavePostProcess(config => config.BloomThreshold = value),
                0f,
                2f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Мягкость порога свечения",
                () => _clientConfig.Config.BloomSoftKnee,
                value => SavePostProcess(config => config.BloomSoftKnee = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Радиус свечения",
                () => _clientConfig.Config.BloomRadius,
                value => SavePostProcess(config => config.BloomRadius = value),
                0.5f,
                8f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Рассеивание свечения",
                () => _clientConfig.Config.BloomScatter,
                value => SavePostProcess(config => config.BloomScatter = value),
                0.1f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundColorControls(
                "Цвет свечения",
                () => _clientConfig.Config.BloomTint,
                value => SavePostProcess(config => config.BloomTint = value),
                0f,
                2f,
                graphicsRefreshers));
            AddPostProcessGroup("Камера и цвет");
            postProcessSection.Add(CreateBoundSlider(
                "Виньетка",
                () => _clientConfig.Config.VignetteIntensity,
                value => SavePostProcess(config => config.VignetteIntensity = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Мягкость виньетки",
                () => _clientConfig.Config.VignetteSmoothness,
                value => SavePostProcess(config => config.VignetteSmoothness = value),
                0.01f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Центр виньетки X",
                () => _clientConfig.Config.VignetteCenter.x,
                value => SavePostProcess(config =>
                    config.VignetteCenter = new Vector2(value, config.VignetteCenter.y)),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Центр виньетки Y",
                () => _clientConfig.Config.VignetteCenter.y,
                value => SavePostProcess(config =>
                    config.VignetteCenter = new Vector2(config.VignetteCenter.x, value)),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundColorControls(
                "Цвет виньетки",
                () => _clientConfig.Config.VignetteColor,
                value => SavePostProcess(config => config.VignetteColor = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Хроматическая аберрация",
                () => _clientConfig.Config.ChromaticAberrationIntensity,
                value => SavePostProcess(
                    config => config.ChromaticAberrationIntensity = value),
                0f,
                0.25f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Экспозиция",
                () => _clientConfig.Config.ColorGradingExposure,
                value => SavePostProcess(config => config.ColorGradingExposure = value),
                -2f,
                2f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Контраст",
                () => _clientConfig.Config.ColorGradingContrast,
                value => SavePostProcess(config => config.ColorGradingContrast = value),
                -0.5f,
                0.5f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Насыщенность",
                () => _clientConfig.Config.ColorGradingSaturation,
                value => SavePostProcess(config => config.ColorGradingSaturation = value),
                0f,
                2f,
                graphicsRefreshers));
            Toggle toneMappingToggle = CreateBoundToggle(
                "Тональное отображение",
                () => _clientConfig.Config.ColorGradingToneMapping,
                value => SavePostProcess(config => config.ColorGradingToneMapping = value),
                graphicsRefreshers);
            postProcessSection.Add(toneMappingToggle);
            postProcessSection.Add(CreateBoundSlider(
                "Белая точка tone mapping",
                () => _clientConfig.Config.ColorGradingToneMappingWhitePoint,
                value => SavePostProcess(
                    config => config.ColorGradingToneMappingWhitePoint = value),
                0.25f,
                8f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundColorControls(
                "Цветовой фильтр",
                () => _clientConfig.Config.ColorGradingFilter,
                value => SavePostProcess(config => config.ColorGradingFilter = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Зернистость",
                () => _clientConfig.Config.EigengrauIntensity,
                value => SavePostProcess(config => config.EigengrauIntensity = value),
                0f,
                0.25f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundColorControls(
                "Цвет зернистости",
                () => _clientConfig.Config.EigengrauColor,
                value => SavePostProcess(config => config.EigengrauColor = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Порог темноты зернистости",
                () => _clientConfig.Config.EigengrauDarknessThreshold,
                value => SavePostProcess(config => config.EigengrauDarknessThreshold = value),
                0.02f,
                0.75f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Масштаб зернистости",
                () => _clientConfig.Config.EigengrauNoiseScale,
                value => SavePostProcess(config => config.EigengrauNoiseScale = value),
                0.75f,
                2f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Скорость зернистости",
                () => _clientConfig.Config.EigengrauAnimationSpeed,
                value => SavePostProcess(config => config.EigengrauAnimationSpeed = value),
                1f,
                60f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Размытие движения",
                () => _clientConfig.Config.MotionBlurIntensity,
                value => SavePostProcess(config => config.MotionBlurIntensity = value),
                0f,
                0.5f,
                graphicsRefreshers));

            AdvancedPostProcessSettings Advanced() =>
                _clientConfig.Config.AdvancedPostProcess;
            void SaveAdvanced(Action<AdvancedPostProcessSettings> update)
            {
                SavePostProcess(config => update(config.AdvancedPostProcess));
            }

            AddPostProcessGroup("Детализация");
            postProcessSection.Add(CreateBoundSlider(
                "Локальная чёткость",
                () => Advanced().LocalContrastIntensity,
                value => SaveAdvanced(settings => settings.LocalContrastIntensity = value),
                0f,
                0.5f,
                graphicsRefreshers));
            AddPostProcessGroup("Оптические эффекты");
            postProcessSection.Add(CreateBoundSlider(
                "Световая пыль на визоре",
                () => Advanced().LensDirtIntensity,
                value => SaveAdvanced(settings => settings.LensDirtIntensity = value),
                0f,
                0.35f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Масштаб световой пыли",
                () => Advanced().LensDirtScale,
                value => SaveAdvanced(settings => settings.LensDirtScale = value),
                0.25f,
                16f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Анаморфные лучи",
                () => Advanced().AnamorphicIntensity,
                value => SaveAdvanced(settings => settings.AnamorphicIntensity = value),
                0f,
                1f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Длина анаморфных лучей",
                () => Advanced().AnamorphicLength,
                value => SaveAdvanced(settings => settings.AnamorphicLength = value),
                0.25f,
                8f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Хроматическая дифракция",
                () => Advanced().ChromaticDiffractionIntensity,
                value => SaveAdvanced(
                    settings => settings.ChromaticDiffractionIntensity = value),
                0f,
                0.5f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Тепловая рефракция",
                () => Advanced().HeatRefractionIntensity,
                value => SaveAdvanced(settings => settings.HeatRefractionIntensity = value),
                0f,
                0.25f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Размер тепловых волн",
                () => Advanced().HeatRefractionScale,
                value => SaveAdvanced(settings => settings.HeatRefractionScale = value),
                0.25f,
                16f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Микроблики материалов",
                () => Advanced().GlintIntensity,
                value => SaveAdvanced(settings => settings.GlintIntensity = value),
                0f,
                0.5f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Порог микробликов",
                () => Advanced().GlintThreshold,
                value => SaveAdvanced(settings => settings.GlintThreshold = value),
                0f,
                4f,
                graphicsRefreshers));
            AddPostProcessGroup("Атмосфера");
            postProcessSection.Add(CreateBoundSlider(
                "Светящаяся пыль",
                () => Advanced().VolumetricDustIntensity,
                value => SaveAdvanced(settings => settings.VolumetricDustIntensity = value),
                0f,
                0.25f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Масштаб светящейся пыли",
                () => Advanced().VolumetricDustScale,
                value => SaveAdvanced(settings => settings.VolumetricDustScale = value),
                0.1f,
                8f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Скорость светящейся пыли",
                () => Advanced().VolumetricDustSpeed,
                value => SaveAdvanced(settings => settings.VolumetricDustSpeed = value),
                0f,
                2f,
                graphicsRefreshers));
            AddPostProcessGroup("Физика дисплея");
            postProcessSection.Add(CreateBoundSlider(
                "Структура люминофора",
                () => Advanced().PhosphorMaskIntensity,
                value => SaveAdvanced(settings => settings.PhosphorMaskIntensity = value),
                0f,
                0.35f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Перцептивный dithering",
                () => Advanced().DitheringIntensity,
                value => SaveAdvanced(settings => settings.DitheringIntensity = value),
                0f,
                1f,
                graphicsRefreshers));
            AddPostProcessGroup("Temporal");
            postProcessSection.Add(CreateBoundSlider(
                "Послесвечение люминофора",
                () => Advanced().TemporalPersistenceIntensity,
                value => SaveAdvanced(
                    settings => settings.TemporalPersistenceIntensity = value),
                0f,
                0.8f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Затухание послесвечения",
                () => Advanced().TemporalPersistenceDecay,
                value => SaveAdvanced(
                    settings => settings.TemporalPersistenceDecay = value),
                0f,
                0.98f,
                graphicsRefreshers));
            postProcessSection.Add(CreateBoundSlider(
                "Temporal stability света",
                () => Advanced().LightStability,
                value => SaveAdvanced(settings => settings.LightStability = value),
                0f,
                0.9f,
                graphicsRefreshers));

            void SaveShaderSetting(Action<ClientConfig> update)
            {
                _graphicsSettings.UpdateCustomWorldMaterialSettings(update);
            }

            worldMaterialsSection.Add(CreateBoundSlider(
                "Скорость shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerSpeedScale,
                value => SaveShaderSetting(
                    config => config.TerrainShimmerSpeedScale = value),
                0f,
                10f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundColorControls(
                "Цвет shimmer террейна",
                () => _clientConfig.Config.TerrainShimmerColor,
                value => SaveShaderSetting(config => config.TerrainShimmerColor = value),
                0f,
                8f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundSlider(
                "Скорость пульсации террейна",
                () => _clientConfig.Config.TerrainPulseSpeedScale,
                value => SaveShaderSetting(config => config.TerrainPulseSpeedScale = value),
                0f,
                10f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundSlider(
                "Излучение поверхности мира",
                () => _clientConfig.Config.TransitEmissionStrength,
                value => SaveShaderSetting(config => config.TransitEmissionStrength = value),
                0f,
                8f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundColorControls(
                "Цвет излучения поверхности",
                () => _clientConfig.Config.TransitEmissionColor,
                value => SaveShaderSetting(config => config.TransitEmissionColor = value),
                0f,
                8f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundSlider(
                "Излучение дальней поверхности",
                () => _clientConfig.Config.PerspectiveEmissionStrength,
                value => SaveShaderSetting(
                    config => config.PerspectiveEmissionStrength = value),
                0f,
                8f,
                graphicsRefreshers));
            worldMaterialsSection.Add(CreateBoundColorControls(
                "Цвет дальней поверхности",
                () => _clientConfig.Config.PerspectiveEmissionColor,
                value => SaveShaderSetting(
                    config => config.PerspectiveEmissionColor = value),
                0f,
                8f,
                graphicsRefreshers));

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
                        config.UiVolume = volume;
                        break;
                }
            });
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
            Debug.Log($"[PauseMenu] Fullscreen: {Screen.fullScreen}");
            if (_fullscreenButton != null)
            {
                _fullscreenButton.text = nextMode == FullScreenMode.Windowed
                    ? "Оконный"
                    : "Полный экран";
            }
        }

        private void OpenMenu()
        {
            _isOpen = true;
            IsMenuOpen = true;
            if (_menuTree != null)
            {
                _menuTree.BringToFront();
                _menuTree.style.display = DisplayStyle.Flex;
            }

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

            if (_menuTree != null)
            {
                _menuTree.style.display = DisplayStyle.None;
            }
        }

        private void OpenSettings()
        {
            foreach (Action refresh in _settingsRefreshers)
            {
                refresh();
            }

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
                () =>
                {
                    CloseMenu();
                    _mainMenuNavigation.ReturnToMainMenu();
                });
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
