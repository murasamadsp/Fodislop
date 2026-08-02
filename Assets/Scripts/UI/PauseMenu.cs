#nullable enable

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
using Fodinae.World.Terrain;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection.Client;
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
        private VisualElement? _menuPanel;
        private VisualElement? _mainPage;
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private InputAction? _escapeAction;
        private float _originalScale;
        private Button? _fullscreenButton;
        private Button? _headlightButton;

        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IAudioSystem _audioSystem = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private TerrainRenderer _terrainRenderer = null!;

        protected void Start()
        {
            _escapeAction = new InputAction("Escape", binding: "<Keyboard>/escape");
            _escapeAction.performed += _ => ToggleMenu();
            _escapeAction.Enable();

            if (_doc.panelSettings == null)
            {
                Debug.LogError("[PauseMenu] PanelSettings не назначен на UIDocument");
                return;
            }

            _originalScale = _doc.panelSettings.scale;

            CreateMenu(_doc.rootVisualElement);
            CloseMenu();

            var savedScale = PlayerPrefs.GetFloat("UIScale", 1f);
            _doc.panelSettings.scale = savedScale;
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                canvas.scaleFactor = savedScale;
            }
        }

        protected void OnDestroy()
        {
            IsMenuOpen = false;

            if (_doc != null && _doc.panelSettings != null)
            {
                _doc.panelSettings.scale = _originalScale;
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

        private static bool IsHeadlightOn()
        {
            return PlayerPrefs.GetInt("UseLight2D", 1) == 1;
        }

        private void CreateMenu(VisualElement root)
        {
            var uss = Resources.Load<StyleSheet>("Styles/PauseMenu");

            _menuPanel = new VisualElement();
            _menuPanel.AddToClassList("pause-overlay");
            if (uss != null)
            {
                _menuPanel.styleSheets.Add(uss);
            }

            var dimmer = new VisualElement();
            dimmer.AddToClassList("pause-dimmer");
            dimmer.pickingMode = PickingMode.Ignore;
            _menuPanel.Add(dimmer);

            _mainPage = new VisualElement();
            _mainPage.AddToClassList("pause-panel");
            _mainPage.Add(CreateTitle("Меню"));
            _mainPage.Add(CreateButton("Продолжить", CloseMenu));
            _mainPage.Add(CreateButton("Настройки", OpenSettings));
            _mainPage.Add(CreateButton("Выйти", QuitGame));

            var debugDivider = new Label("═════ Отладка ═════");
            debugDivider.AddToClassList("pause-debug-divider");
            _mainPage.Add(debugDivider);

            _mainPage.Add(CreateButton("Тест: Kick сервером", () =>
            {
                var conn = (_connectionService as ConnectionManager)?.Connection as DummyConnection;
                conn?.TriggerDisconnect("Тестовый дисконнект от сервера");
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Reconnect", () =>
            {
                var conn = (_connectionService as ConnectionManager)?.Connection as DummyConnection;
                conn?.TriggerReconnect("Сервер перезагружается");
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Открыть URL", () =>
            {
                _networkService.Send(new ElementClickPacket("open_url_test", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест модального окна", () =>
            {
                _networkService.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Вступить в клан", () =>
            {
                _networkService.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Выйти из клана", () =>
            {
                _networkService.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Стрелка миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("test_mission_arrow", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Миссии", () =>
            {
                _networkService.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Стены ✗", () =>
            {
                var player = PlayerMovementController.LocalPlayer;
                if (player != null)
                {
                    player.IgnoreCollision = !player.IgnoreCollision;
                    CloseMenu();
                }
            }));

            _menuPanel.Add(_mainPage);

            _settingsPage = new VisualElement();
            _settingsPage.AddToClassList("pause-panel");
            _settingsPage.AddToClassList("pause-settings");
            _settingsPage.Add(CreateTitle("Настройки"));

            var scrollContainer = new ScrollView(ScrollViewMode.Vertical);
            scrollContainer.AddToClassList("pause-scroll");

            scrollContainer.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master, "Audio_Master", 1f));
            scrollContainer.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX, "Audio_SFX", 1f));
            scrollContainer.Add(CreateAudioSlider("Музыка", AudioBusType.Music, "Audio_Music", 0.5f));
            scrollContainer.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience, "Audio_Ambience", 0.7f));
            scrollContainer.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice, "Audio_Voice", 1f));
            scrollContainer.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI, "Audio_UI", 1f));

            scrollContainer.Add(CreateSlider(
                "Масштаб UI",
                PlayerPrefs.GetFloat("UIScale", 1f),
                v =>
                {
                    PlayerPrefs.SetFloat("UIScale", v);
                    PlayerPrefs.Save();
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
                2f));

            scrollContainer.Add(CreateLabel("Экран"));

            _fullscreenButton = new Button(ToggleFullscreen);
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
            _fullscreenButton.AddToClassList("pause-btn");
            scrollContainer.Add(_fullscreenButton);

            scrollContainer.Add(CreateLabel("Разрешение"));

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
            scrollContainer.Add(resolutionButton);

            scrollContainer.Add(CreateLabel("Графика"));

            string[] lightingQualityNames =
            [
                "Низкое",
                "Среднее",
                "Высокое",
                "Ультра",
            ];
            int savedQuality = Mathf.Clamp(
                PlayerPrefs.GetInt("WorldLightingQuality", (int)TerrariaLightingEngine.QualityPreset.Ultra),
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
                PlayerPrefs.SetInt("WorldLightingQuality", (int)quality);
                PlayerPrefs.Save();
                UpdateLightingQualityButton();
            };
            lightingQuality.AddToClassList("pause-btn");
            UpdateLightingQualityButton();
            scrollContainer.Add(lightingQuality);

            var ambientOcclusionToggle = new Toggle("Ambient Occlusion (AO)")
            {
                value = PlayerPrefs.GetInt(
                    TerrariaLightingEngine.AmbientOcclusionPreferenceKey,
                    1) == 1,
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

                PlayerPrefs.SetInt(
                    TerrariaLightingEngine.AmbientOcclusionPreferenceKey,
                    evt.newValue ? 1 : 0);
                PlayerPrefs.Save();
            });
            scrollContainer.Add(ambientOcclusionToggle);

            var globalIlluminationToggle = new Toggle("Глобальное освещение (GI)")
            {
                value = PlayerPrefs.GetInt(
                    TerrariaLightingEngine.GlobalIlluminationPreferenceKey,
                    1) == 1,
            };
            globalIlluminationToggle.RegisterValueChangedCallback(evt =>
            {
                var engine = TerrariaLightingEngine.Instance
                    ?? FindAnyObjectByType<TerrariaLightingEngine>();
                if (engine != null)
                {
                    engine.SetGlobalIlluminationEnabled(evt.newValue);
                    return;
                }

                PlayerPrefs.SetInt(
                    TerrariaLightingEngine.GlobalIlluminationPreferenceKey,
                    evt.newValue ? 1 : 0);
                PlayerPrefs.Save();
            });
            scrollContainer.Add(globalIlluminationToggle);

            void ApplyLightingSetting(
                string preferenceKey,
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

                PlayerPrefs.SetFloat(preferenceKey, value);
                PlayerPrefs.Save();
            }

            scrollContainer.Add(CreateLabel("Параметры освещения"));
            scrollContainer.Add(CreateSlider(
                "Яркость окружения",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.AmbientIntensityPreferenceKey,
                    0.9f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.AmbientIntensityPreferenceKey,
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                2f));
            scrollContainer.Add(CreateSlider(
                "Мощность излучения",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.EmissionScalePreferenceKey,
                    8f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.EmissionScalePreferenceKey,
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                16f));
            scrollContainer.Add(CreateSlider(
                "Поглощение пустой средой",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.EmptyExtinctionPreferenceKey,
                    1f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.EmptyExtinctionPreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                4f));
            scrollContainer.Add(CreateSlider(
                "Поглощение блоками",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.SolidExtinctionPreferenceKey,
                    1f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.SolidExtinctionPreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                4f));
            scrollContainer.Add(CreateSlider(
                "Сила GI bounce",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.BounceStrengthPreferenceKey,
                    0.3f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.BounceStrengthPreferenceKey,
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                2f));
            scrollContainer.Add(CreateSlider(
                "Радиус AO",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.AmbientOcclusionRadiusPreferenceKey,
                    2f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.AmbientOcclusionRadiusPreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f));
            scrollContainer.Add(CreateSlider(
                "Сила AO",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.AmbientOcclusionStrengthPreferenceKey,
                    1.5f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.AmbientOcclusionStrengthPreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f));
            scrollContainer.Add(CreateSlider(
                "Физический радиус источника",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.EmitterRadiusPreferenceKey,
                    1f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.EmitterRadiusPreferenceKey,
                    value,
                    static (engine, setting) => engine.SetDynamicEmitterRadius(setting)),
                0.1f,
                2f));
            scrollContainer.Add(CreateSlider(
                "Максимум светового множителя",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.MaximumMultiplierPreferenceKey,
                    1f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.MaximumMultiplierPreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.5f,
                4f));
            scrollContainer.Add(CreateSlider(
                "Дистанция debug transmittance",
                PlayerPrefs.GetFloat(
                    TerrariaLightingEngine.TransmittanceDistancePreferenceKey,
                    10f),
                value => ApplyLightingSetting(
                    TerrariaLightingEngine.TransmittanceDistancePreferenceKey,
                    value,
                    static (engine, setting) =>
                        engine.SetTransmittanceDebugDistance(setting)),
                2f,
                32f));

            string[] lightingDebugNames =
            [
                "Итоговый свет",
                "Occupancy",
                "Albedo",
                "Emission",
                "Local transmittance",
                "Direct",
                "GI bounce",
                "Ambient Occlusion",
            ];
            int activeDebugView = (int)(TerrariaLightingEngine.Instance?.ActiveDebugView ??
                TerrariaLightingEngine.DebugView.Composite);
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
            scrollContainer.Add(lightingDebugView);

            scrollContainer.Add(CreateLabel("Постобработка"));

            if (PostProcessController.Instance != null)
            {
                var pp = PostProcessController.Instance;
                scrollContainer.Add(CreateSlider("Свечение (Bloom)", pp.BloomIntensity, v => pp.BloomIntensity = v, 0f, 5f));
                scrollContainer.Add(CreateSlider("Виньетка", pp.VignetteIntensity, v => pp.VignetteIntensity = v, 0f, 1f));
                scrollContainer.Add(CreateSlider("Хроматическая аберрация", pp.ChromaticAberrationIntensity, v => pp.ChromaticAberrationIntensity = v, 0f, 1f));
                scrollContainer.Add(CreateSlider("Зернистость (Eigengrau)", pp.EigengrauIntensity, v => pp.EigengrauIntensity = v, 0f, 1f));
                scrollContainer.Add(CreateSlider("Размытие движения", pp.MotionBlurIntensity, v => pp.MotionBlurIntensity = v, 0f, 1f));
            }

            scrollContainer.Add(CreateLabel("Аура игрока"));

            _headlightButton = new Button(ToggleHeadlight);
            _headlightButton.text = IsHeadlightOn() ? "Вкл" : "Выкл";
            _headlightButton.AddToClassList("pause-btn");
            scrollContainer.Add(_headlightButton);

            _settingsPage.Add(scrollContainer);
            _settingsPage.Add(CreateButton("Назад", CloseSettings));
            _settingsPage.style.display = DisplayStyle.None;
            _menuPanel.Add(_settingsPage);

            root.Add(_menuPanel);
        }

        private VisualElement CreateAudioSlider(string title, AudioBusType busType, string prefKey, float defaultValue)
        {
            float currentVol = _audioSystem != null ? _audioSystem.GetBusVolume(busType) : PlayerPrefs.GetFloat(prefKey, defaultValue);
            return CreateSlider(
                title,
                currentVol,
                v =>
                {
                    _audioSystem?.SetBusVolume(busType, v);
                    PlayerPrefs.SetFloat(prefKey, v);
                    PlayerPrefs.Save();
                },
                0f,
                1f);
        }

        private Button CreateButton(string text, System.Action action)
        {
            var btn = new Button(action);
            btn.text = text;
            btn.AddToClassList("pause-btn");
            return btn;
        }

        private Label CreateTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("pause-title");
            return label;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("pause-slider-label");
            return label;
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

        private void ToggleHeadlight()
        {
            bool current = PlayerPrefs.GetInt("UseLight2D", 1) == 1;
            bool newValue = !current;

            var terrain = _terrainRenderer;
            if (terrain != null)
            {
                terrain.SetUseLight2D(newValue);
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                var headlight = player.GetComponent<RobotHeadlight>();
                if (headlight == null && newValue)
                {
                    headlight = player.gameObject.AddComponent<RobotHeadlight>();
                }

                if (headlight != null)
                {
                    headlight.SetEnabled(newValue);
                }
            }

            PlayerPrefs.SetInt("UseLight2D", newValue ? 1 : 0);
            PlayerPrefs.Save();
            if (_headlightButton != null)
            {
                _headlightButton.text = newValue ? "Вкл" : "Выкл";
            }
        }

        private void OpenMenu()
        {
            _isOpen = true;
            IsMenuOpen = true;
            if (_menuPanel != null)
            {
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
            }
        }

        private void SendClientConfig()
        {
            var context = new List<StringPairPacket>();

            context.Add(new StringPairPacket("master_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.Master) ?? PlayerPrefs.GetFloat("Audio_Master", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("sfx_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.SFX) ?? PlayerPrefs.GetFloat("Audio_SFX", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("music_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.Music) ?? PlayerPrefs.GetFloat("Audio_Music", 0.5f)) * 255)).ToString()));
            context.Add(new StringPairPacket("ambience_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.Ambience) ?? PlayerPrefs.GetFloat("Audio_Ambience", 0.7f)) * 255)).ToString()));
            context.Add(new StringPairPacket("voice_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.Voice) ?? PlayerPrefs.GetFloat("Audio_Voice", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("ui_volume", ((byte)((_audioSystem?.GetBusVolume(AudioBusType.UI) ?? PlayerPrefs.GetFloat("Audio_UI", 1f)) * 255)).ToString()));

            context.Add(new StringPairPacket("headlight", IsHeadlightOn() ? "true" : "false"));
            context.Add(new StringPairPacket("ui_scale", PlayerPrefs.GetFloat("UIScale", 1f).ToString("F2")));

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

            var panel = new VisualElement();
            panel.AddToClassList("pause-confirm-panel");

            var titleLabel = new Label("Выход из игры");
            titleLabel.AddToClassList("pause-confirm-title");
            panel.Add(titleLabel);

            var descLabel = new Label("Вы уверены, что хотите выйти?");
            descLabel.AddToClassList("pause-confirm-desc");
            panel.Add(descLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("pause-confirm-buttons");

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
