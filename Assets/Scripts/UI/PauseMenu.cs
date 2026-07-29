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
using Fodinae.UI.Programmator;
using Fodinae.World;
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

        private UIDocument _doc;
        private VisualElement _menuPanel;
        private VisualElement _mainPage;
        private VisualElement _settingsPage;
        private bool _isOpen;
        private InputAction _escapeAction;
        private float _originalScale;
        private Button _fullscreenButton;
        private Button _simpleGraphicsButton;
        private Button _headlightButton;

        [Inject]
        private INetworkService _networkService;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker;
        [Inject]
        private TerrainRenderer _terrainRenderer;

        protected void Start()
        {
            _escapeAction = new InputAction("Escape", binding: "<Keyboard>/escape");
            _escapeAction.performed += _ => ToggleMenu();
            _escapeAction.Enable();

            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[PauseMenu] UIDocument не найден");
                return;
            }

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

            var label = new Label(labelText);
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(min, max);
            slider.value = initialValue;
            slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            container.Add(slider);

            return container;
        }

        private static bool IsSimpleGraphics()
        {
            return PlayerPrefs.GetInt("SimpleGraphics", 0) == 1;
        }

        private static bool IsHeadlightOn()
        {
            return PlayerPrefs.GetInt("UseLight2D", 0) == 1;
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
                var conn = ConnectionManager.Instance?.Connection as DummyConnection;
                conn?.TriggerDisconnect("Тестовый дисконнект от сервера");
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Reconnect", () =>
            {
                var conn = ConnectionManager.Instance?.Connection as DummyConnection;
                conn?.TriggerReconnect("Сервер перезагружается");
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Открыть URL", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("open_url_test", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест модального окна", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Вступить в клан", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Выйти из клана", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Тест: Стрелка миссии", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("test_mission_arrow", 0, System.Array.Empty<StringPairPacket>()));
                CloseMenu();
            }));

            _mainPage.Add(CreateButton("Миссии", () =>
            {
                NetworkService.Instance?.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
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
                    _doc.panelSettings.scale = v;
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

            var resOptions = new System.Collections.Generic.List<string>();
            foreach (var res in uniqueResolutions)
            {
                resOptions.Add($"{res.width} x {res.height}");
            }

            var resDropdown = new DropdownField(resOptions, currentResIndex);
            resDropdown.RegisterValueChangedCallback(evt =>
            {
                var index = resDropdown.index;
                if (index >= 0 && index < uniqueResolutions.Count)
                {
                    var res = uniqueResolutions[index];
                    Screen.SetResolution(res.width, res.height, Screen.fullScreen);
                    Debug.Log($"[PauseMenu] Resolution: {res.width}x{res.height}");
                }
            });
            scrollContainer.Add(resDropdown);

            scrollContainer.Add(CreateLabel("Графика"));

            _simpleGraphicsButton = new Button(ToggleSimpleGraphics);
            _simpleGraphicsButton.text = IsSimpleGraphics() ? "Простая" : "Обычная";
            _simpleGraphicsButton.AddToClassList("pause-btn");
            scrollContainer.Add(_simpleGraphicsButton);

            scrollContainer.Add(CreateLabel("Фары"));

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

        private static VisualElement CreateAudioSlider(string title, AudioBusType busType, string prefKey, float defaultValue)
        {
            var audio = ServiceLocator.Resolve<IAudioSystem>();
            float currentVol = audio != null ? audio.GetBusVolume(busType) : PlayerPrefs.GetFloat(prefKey, defaultValue);
            return CreateSlider(
                title,
                currentVol,
                v =>
                {
                    audio?.SetBusVolume(busType, v);
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

            if (_settingsPage.style.display == DisplayStyle.Flex)
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
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
        }

        private void ToggleSimpleGraphics()
        {
            var terrain = _terrainRenderer;
            if (terrain == null)
            {
                return;
            }

            bool current = PlayerPrefs.GetInt("SimpleGraphics", 0) == 1;
            bool newValue = !current;
            terrain.SetSimpleGraphics(newValue);
            PlayerPrefs.SetInt("SimpleGraphics", newValue ? 1 : 0);
            PlayerPrefs.Save();
            _simpleGraphicsButton.text = newValue ? "Простая" : "Обычная";
        }

        private void ToggleHeadlight()
        {
            bool current = PlayerPrefs.GetInt("UseLight2D", 0) == 1;
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
            _headlightButton.text = newValue ? "Вкл" : "Выкл";
        }

        private void OpenMenu()
        {
            _isOpen = true;
            IsMenuOpen = true;
            _menuPanel.style.display = DisplayStyle.Flex;
            _mainPage.style.display = DisplayStyle.Flex;
            _settingsPage.style.display = DisplayStyle.None;
        }

        private void CloseMenu()
        {
            SendClientConfig();
            _isOpen = false;
            IsMenuOpen = false;
            _menuPanel.style.display = DisplayStyle.None;
        }

        private void SendClientConfig()
        {
            var context = new List<StringPairPacket>();
            var audio = ServiceLocator.Resolve<IAudioSystem>();

            context.Add(new StringPairPacket("master_volume", ((byte)((audio?.GetBusVolume(AudioBusType.Master) ?? PlayerPrefs.GetFloat("Audio_Master", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("sfx_volume", ((byte)((audio?.GetBusVolume(AudioBusType.SFX) ?? PlayerPrefs.GetFloat("Audio_SFX", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("music_volume", ((byte)((audio?.GetBusVolume(AudioBusType.Music) ?? PlayerPrefs.GetFloat("Audio_Music", 0.5f)) * 255)).ToString()));
            context.Add(new StringPairPacket("ambience_volume", ((byte)((audio?.GetBusVolume(AudioBusType.Ambience) ?? PlayerPrefs.GetFloat("Audio_Ambience", 0.7f)) * 255)).ToString()));
            context.Add(new StringPairPacket("voice_volume", ((byte)((audio?.GetBusVolume(AudioBusType.Voice) ?? PlayerPrefs.GetFloat("Audio_Voice", 1f)) * 255)).ToString()));
            context.Add(new StringPairPacket("ui_volume", ((byte)((audio?.GetBusVolume(AudioBusType.UI) ?? PlayerPrefs.GetFloat("Audio_UI", 1f)) * 255)).ToString()));

            context.Add(new StringPairPacket("renderer", IsSimpleGraphics() ? "Simplified" : "Default"));
            context.Add(new StringPairPacket("headlight", IsHeadlightOn() ? "true" : "false"));
            context.Add(new StringPairPacket("ui_scale", PlayerPrefs.GetFloat("UIScale", 1f).ToString("F2")));

            Debug.Log($"[PauseMenu] Sending save_client_config with {context.Count} entries");
            NetworkService.Instance?.Send(new ElementClickPacket("save_client_config", 0, context));
        }

        private void OpenSettings()
        {
            _mainPage.style.display = DisplayStyle.None;
            _settingsPage.style.display = DisplayStyle.Flex;
        }

        private void CloseSettings()
        {
            _settingsPage.style.display = DisplayStyle.None;
            _mainPage.style.display = DisplayStyle.Flex;
        }

        private void QuitGame()
        {
            ShowQuitConfirmation();
        }

        private void ShowQuitConfirmation()
        {
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
