#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
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
        private LightingEngine _lightingEngine = null!;
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
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private float _originalScale;
        private bool _originalScaleCaptured;
        private readonly List<Action> _settingsRefreshers = [];
        private bool _initialized;
        private bool _initializationFailed;

        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IAudioSystem _audioSystem = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;

        private PauseMenuSettingsBuilder? _settingsBuilder;

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
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

            if (_resolver != null)
            {
                if (_clientConfig == null && _resolver.TryResolve(out IClientConfigManager clientConfig)) _clientConfig = clientConfig;
                if (_networkService == null && _resolver.TryResolve(out INetworkService networkService)) _networkService = networkService;
                if (_audioSystem == null && _resolver.TryResolve(out IAudioSystem audioSystem)) _audioSystem = audioSystem;
                if (_connectionService == null && _resolver.TryResolve(out IConnectionService connectionService)) _connectionService = connectionService;
                if (_inputBlocker == null && _resolver.TryResolve(out IInputBlocker inputBlocker)) _inputBlocker = inputBlocker;
                if (_lightingEngine == null && _resolver.TryResolve(out LightingEngine lightingEngine)) _lightingEngine = lightingEngine;
                if (_postProcessController == null && _resolver.TryResolve(out PostProcessController postProcessController)) _postProcessController = postProcessController;
                if (_terrainRenderer == null && _resolver.TryResolve(out TerrainRenderer terrainRenderer)) _terrainRenderer = terrainRenderer;
                if (_graphicsSettings == null && _resolver.TryResolve(out GraphicsSettingsController graphicsSettings)) _graphicsSettings = graphicsSettings;
                if (_displayManager == null && _resolver.TryResolve(out DisplayManager displayManager)) _displayManager = displayManager;
            }

            if (_clientConfig == null || _clientConfig.Config == null || _networkService == null ||
                _audioSystem == null || _connectionService == null || _inputBlocker == null ||
                _lightingEngine == null || _postProcessController == null || _terrainRenderer == null ||
                _graphicsSettings == null || _displayManager == null)
            {
                return;
            }

            if (!_lightingEngine.IsInitialized)
            {
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
                Debug.LogWarning($"[PauseMenu] Menu unavailable: {exception.Message}");
                _initializationFailed = true;
                return;
            }

            HideMenu();

            var savedScale = _clientConfig.Config.UIScale;
            if (Mathf.Abs(_doc.panelSettings.scale - savedScale) > 0.0001f)
            {
                _doc.panelSettings.scale = savedScale;
            }

            _initialized = true;
        }

        private void OnDestroy()
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
            // Static main-page buttons live in PauseMenu.uxml; the scroll container
            // itself is validated for the UXML contract even though it is not
            // modified from code anymore.
            _ = menuTree.Q<ScrollView>("MainPageScroll") ??
                throw new InvalidOperationException("[PauseMenu] MainPageScroll is missing from PauseMenu.uxml.");
            Button resumeButton = menuTree.Q<Button>("ResumeButton") ??
                throw new InvalidOperationException("[PauseMenu] ResumeButton is missing from PauseMenu.uxml.");
            resumeButton.clicked += CloseMenu;
            Button settingsButton = menuTree.Q<Button>("SettingsButton") ??
                throw new InvalidOperationException("[PauseMenu] SettingsButton is missing from PauseMenu.uxml.");
            settingsButton.clicked += OpenSettings;
            Button mainMenuButton = menuTree.Q<Button>("MainMenuButton") ??
                throw new InvalidOperationException("[PauseMenu] MainMenuButton is missing from PauseMenu.uxml.");
            mainMenuButton.clicked += ExitToMainMenu;
            Button quitButton = menuTree.Q<Button>("QuitButton") ??
                throw new InvalidOperationException("[PauseMenu] QuitButton is missing from PauseMenu.uxml.");
            quitButton.clicked += QuitGame;
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

            _settingsRefreshers.Clear();
            _settingsBuilder = new PauseMenuSettingsBuilder(
                _doc,
                _clientConfig,
                _audioSystem,
                _displayManager,
                _graphicsSettings,
                _lightingEngine,
                _postProcessController,
                _networkService,
                _connectionService,
                _settingsRefreshers,
                CloseMenu);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Built first: BuildAdvancedPage appends the lighting debug view
            // and the diagnostics readout to this section.
            VisualElement debugSection = _settingsBuilder.BuildDebugSection();
#endif

            _settingsBuilder.BuildAudioPage(audioScroll);
            _settingsBuilder.BuildDisplayPage(displayScroll);
            _settingsBuilder.BuildGraphicsPage(graphicsScroll);
            _settingsBuilder.BuildEffectsPage(effectsScroll);
            _settingsBuilder.BuildInterfacePage(interfaceScroll);
            _settingsBuilder.BuildAdvancedPage(advancedScroll);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            advancedScroll.contentContainer.Add(debugSection);
#endif

            // Apply the initial page after all dynamic content has been attached.
            // ScrollView owns its content container; adding sections directly to it
            // can leave the viewport empty after a domain reload.
            ShowSettingsPage(0);

            _settingsPage.style.display = DisplayStyle.None;
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
            PauseMenuUIFactory.ShowConfirmation(
                _doc,
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
            PauseMenuUIFactory.ShowConfirmation(
                _doc,
                "Выйти в главное меню",
                "Вы уверены? Текущая сессия будет закрыта.",
                "В меню",
                () =>
                {
                    CloseMenu();
                    _mainMenuNavigation.ReturnToMainMenu();
                });
        }
    }
}
