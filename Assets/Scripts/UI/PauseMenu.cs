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

        private float GetConfiguredBusVolume(AudioBusType busType, string preferenceKey, float defaultValue)
        {
            if (_audioSystem is AudioSystem audioSystem && audioSystem.IsInitialized)
            {
                return audioSystem.GetBusVolume(busType);
            }

            return PlayerPrefs.GetFloat(preferenceKey, defaultValue);
        }

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
            Label? lightingSectionLabel = null;
            var lightingJumpButton = new Button(() =>
            {
                if (lightingSectionLabel != null)
                {
                    scrollContainer.ScrollTo(lightingSectionLabel);
                }
            })
            {
                text = "Перейти к настройкам освещения",
            };
            lightingJumpButton.AddToClassList("pause-btn");
            scrollContainer.Add(lightingJumpButton);

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
                1f));

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
            TerrariaLightingEngine? qualityEngine = TerrariaLightingEngine.Instance
                ?? FindAnyObjectByType<TerrariaLightingEngine>();
            int savedQuality = Mathf.Clamp(
                (int)(qualityEngine?.Quality ?? LightingDefaults.Quality),
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
                UpdateLightingQualityButton();
            };
            lightingQuality.AddToClassList("pause-btn");
            UpdateLightingQualityButton();
            scrollContainer.Add(lightingQuality);

            var ambientOcclusionToggle = new Toggle("Контактное затенение (AO)")
            {
                value = (TerrariaLightingEngine.Instance ??
                    FindAnyObjectByType<TerrariaLightingEngine>())?.AmbientOcclusionEnabled ??
                    LightingDefaults.AmbientOcclusionEnabled,
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
            scrollContainer.Add(ambientOcclusionToggle);

            var globalIlluminationToggle = new Toggle("Непрямой диффузный свет")
            {
                value = (TerrariaLightingEngine.Instance ??
                    FindAnyObjectByType<TerrariaLightingEngine>())?.DiffuseBounceEnabled ??
                    LightingDefaults.DiffuseBounceEnabled,
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
            scrollContainer.Add(globalIlluminationToggle);

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

            lightingSectionLabel = CreateLabel("Параметры освещения");
            scrollContainer.Add(lightingSectionLabel);
            scrollContainer.Add(CreateSlider(
                "Яркость окружения",
                GetLightingValue(
                    LightingDefaults.AmbientIntensity,
                    0f,
                    1f,
                    static engine => engine.AmbientIntensity),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetAmbientIntensity(setting)),
                0f,
                1f));
            scrollContainer.Add(CreateSlider(
                "Мощность излучения",
                GetLightingValue(
                    LightingDefaults.EmissionScale,
                    0.1f,
                    8f,
                    static engine => engine.EmissionScale),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetEmissionScale(setting)),
                0.1f,
                8f));
            scrollContainer.Add(CreateLabel("Динамические источники"));
            Robot? localRobot = PlayerMovementController.LocalPlayer?.GetComponent<Robot>();
            Robot? GetLocalRobot() => PlayerMovementController.LocalPlayer?.GetComponent<Robot>() ?? localRobot;
            float dynamicLightIntensity = localRobot?.DynamicLightIntensity ??
                LightingDefaults.DynamicLightIntensity;
            Color dynamicLightColor = localRobot?.DynamicLightColor ?? LightingDefaults.DynamicLightColor;
            scrollContainer.Add(CreateSlider(
                "Мощность emission игрока",
                dynamicLightIntensity,
                value => GetLocalRobot()?.SetDynamicLightIntensity(value),
                0f,
                4f));
            TerrariaLightingEngine? dynamicLightingEngine = TerrariaLightingEngine.Instance
                ?? FindAnyObjectByType<TerrariaLightingEngine>();
            scrollContainer.Add(CreateSlider(
                "Частота расчёта dynamic emission",
                dynamicLightingEngine?.DynamicLightUpdatesPerSecond ??
                    LightingDefaults.DynamicLightUpdatesPerSecond,
                value => dynamicLightingEngine?.SetDynamicLightUpdatesPerSecond(value),
                1f,
                LightingDefaults.DynamicLightUpdatesPerSecondLimit));

            System.Action<float> setDynamicLightRed = value =>
            {
                Robot? robot = GetLocalRobot();
                if (robot != null)
                {
                    Color color = robot.DynamicLightColor;
                    robot.SetDynamicLightColor(new Color(value, color.g, color.b, 1f));
                }
            };
            scrollContainer.Add(CreateSlider(
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
            scrollContainer.Add(CreateSlider(
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
            scrollContainer.Add(CreateSlider(
                "Цвет источника: синий",
                dynamicLightColor.b,
                setDynamicLightBlue,
                0f,
                1f));

            scrollContainer.Add(CreateLabel("Физическое поглощение"));
            scrollContainer.Add(CreateSlider(
                "Ослабление света в пустой среде",
                GetLightingValue(
                    LightingDefaults.EmptyExtinctionMultiplier,
                    0f,
                    2f,
                    static engine => engine.EmptyExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetEmptyExtinctionMultiplier(setting)),
                0f,
                2f));
            scrollContainer.Add(CreateSlider(
                "Ослабление света физической массой",
                GetLightingValue(
                    LightingDefaults.SolidExtinctionMultiplier,
                    0.25f,
                    2f,
                    static engine => engine.SolidExtinctionMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetSolidExtinctionMultiplier(setting)),
                0.25f,
                2f));
            scrollContainer.Add(CreateLabel("Непрямой диффузный свет"));
            scrollContainer.Add(CreateSlider(
                "Сила непрямого диффузного света",
                GetLightingValue(
                    LightingDefaults.BounceStrength,
                    0f,
                    1f,
                    static engine => engine.BounceStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetBounceStrength(setting)),
                0f,
                1f));
            scrollContainer.Add(CreateLabel("Контактное затенение"));
            scrollContainer.Add(CreateSlider(
                "Радиус контактного AO",
                GetLightingValue(
                    LightingDefaults.AmbientOcclusionRadiusCells,
                    0.5f,
                    8f,
                    static engine => engine.AmbientOcclusionRadiusCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionRadius(setting)),
                0.5f,
                8f));
            scrollContainer.Add(CreateSlider(
                "Интенсивность контактного AO",
                GetLightingValue(
                    LightingDefaults.AmbientOcclusionStrength,
                    0.1f,
                    8f,
                    static engine => engine.AmbientOcclusionStrength),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetAmbientOcclusionStrength(setting)),
                0.1f,
                8f));
            scrollContainer.Add(CreateLabel("Диагностика и границы расчёта"));
            scrollContainer.Add(CreateSlider(
                "Максимум светового множителя",
                GetLightingValue(
                    LightingDefaults.MaximumLightMultiplier,
                    0.25f,
                    LightingDefaults.MaximumLightMultiplierLimit,
                    static engine => engine.MaximumLightMultiplier),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetMaximumLightMultiplier(setting)),
                0.25f,
                LightingDefaults.MaximumLightMultiplierLimit));
            scrollContainer.Add(CreateSlider(
                "Пропускание света — диагностика",
                GetLightingValue(
                    LightingDefaults.TransmittanceDebugDistanceCells,
                    2f,
                    32f,
                    static engine => engine.TransmittanceDebugDistanceCells),
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) =>
                        engine.SetTransmittanceDebugDistance(setting)),
                2f,
                32f));
            scrollContainer.Add(CreateSlider(
                "Минимальное пропускание каскадов",
                GetLightingValue(
                    LightingDefaults.MinimumTransmission,
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
                LightingDefaults.LightSafeBorder;
            scrollContainer.Add(CreateSlider(
                "Безопасная граница света",
                currentLightSafeBorder,
                value => ApplyLightingSetting(
                    value,
                    static (engine, setting) => engine.SetLightSafeBorder(setting)),
                0f,
                8f));

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
            scrollContainer.Add(lightingDebugView);

            scrollContainer.Add(CreateLabel("Фактические параметры lighting"));
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
            scrollContainer.Add(lightingDiagnostics);
            var refreshLightingDiagnostics = new Button(UpdateLightingDiagnostics)
            {
                text = "Обновить параметры lighting",
            };
            refreshLightingDiagnostics.AddToClassList("pause-btn");
            scrollContainer.Add(refreshLightingDiagnostics);
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
            scrollContainer.Add(resetLightingPreferences);

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

            context.Add(new StringPairPacket("master_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Master, "Audio_Master", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("sfx_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.SFX, "Audio_SFX", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("music_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Music, "Audio_Music", 0.5f) * 255)).ToString()));
            context.Add(new StringPairPacket("ambience_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Ambience, "Audio_Ambience", 0.7f) * 255)).ToString()));
            context.Add(new StringPairPacket("voice_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.Voice, "Audio_Voice", 1f) * 255)).ToString()));
            context.Add(new StringPairPacket("ui_volume", ((byte)(GetConfiguredBusVolume(AudioBusType.UI, "Audio_UI", 1f) * 255)).ToString()));

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
