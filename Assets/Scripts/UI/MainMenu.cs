#nullable enable

using System;
using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        [Inject]
        private IConnectionService _connectionService = null!;

        [SerializeField]
        private Texture2D? _loaderTexture;
        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private VisualElement? _loaderContainer;
        private Image? _loaderImage;
        private Button? _playButton;
        private GameManager? _gameManager;
        private bool _built;
        private bool _playButtonSubscribed;

        protected void OnEnable()
        {
            if (_built)
            {
                SubscribePlayButton();
                return;
            }

            _doc = GetComponent<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                throw new InvalidOperationException(
                    "MainMenu requires a UIDocument with a ready rootVisualElement.");
            }

            _root = _doc.rootVisualElement;

            var mainMenuUXML = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.MainMenuUxml);
            if (mainMenuUXML == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/MainMenu.uxml' was not found.");
            }

            VisualElement tree = mainMenuUXML.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);
            _tree = tree;

            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? throw new InvalidDataException(
                "MainMenu.uxml is missing the required 'MainMenuContainer' element.");
            _loaderContainer = tree.Q<VisualElement>("LoaderContainer") ?? throw new InvalidDataException(
                "MainMenu.uxml is missing the required 'LoaderContainer' element.");
            _loaderImage = tree.Q<Image>("LoaderImage") ?? throw new InvalidDataException(
                "MainMenu.uxml is missing the required 'LoaderImage' element.");
            _playButton = tree.Q<Button>("PlayButton") ?? throw new InvalidDataException(
                "MainMenu.uxml is missing the required 'PlayButton' element.");
            if (_loaderContainer != null)
            {
                _loaderContainer.pickingMode = PickingMode.Ignore;
            }

            if (_loaderImage != null)
            {
                _loaderImage.pickingMode = PickingMode.Ignore;
            }

            SubscribePlayButton();

            if (_loaderImage != null)
            {
                Texture2D texture = _loaderTexture ?? throw new InvalidOperationException(
                    "MainMenu requires an explicit loader texture assigned in the scene.");
                _loaderImage.image = texture;
            }

            _built = true;
            Debug.Log($"[MainMenu] UI BUILT: rootChildren={_root.childCount}, panel={(_doc.panelSettings != null ? _doc.panelSettings.name : "NULL")}");
        }

        protected void OnDisable()
        {
            if (_playButtonSubscribed && _playButton != null)
            {
                _playButton.clicked -= OnPlayButtonClicked;
                _playButtonSubscribed = false;
            }
        }

        private void SubscribePlayButton()
        {
            if (_playButtonSubscribed || _playButton == null)
            {
                return;
            }

            _playButton.clicked += OnPlayButtonClicked;
            _playButtonSubscribed = true;
        }

        protected void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void HideLoader()
        {
            if (_loaderContainer != null)
            {
                _loaderContainer.style.display = DisplayStyle.None;
                Debug.Log("[MainMenu] Loader hidden");
            }
        }

        private void HideMenu()
        {
            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.None;
                Debug.Log("[MainMenu] Menu hidden");
            }
        }

        private void OnWorldLoaded()
        {
            HideLoader();
            HideMenu();

            if (_tree != null)
            {
                _tree.style.display = DisplayStyle.None;
                _tree.pickingMode = PickingMode.Ignore;
                Debug.Log("[MainMenu] Fullscreen layer hidden");
            }

            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }
        }

        private void OnPlayButtonClicked()
        {
            Debug.Log("[MainMenu] Play button clicked");

            HideMenu();

            _gameManager ??= ServiceLocator.Resolve<GameManager>();
            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
                _gameManager.OnWorldLoaded += OnWorldLoaded;
            }

            var connectionService = _connectionService ?? ServiceLocator.Resolve<IConnectionService>();
            if (connectionService != null && !connectionService.IsConnected)
            {
                connectionService.Connect(oldClient: false);
            }
            else
            {
                Debug.LogWarning($"[MainMenu] Cannot connect: connectionService={(connectionService != null ? "ok" : "NULL")}, IsConnected={(connectionService != null ? connectionService.IsConnected.ToString() : "N/A")}");
            }
        }
    }
}
