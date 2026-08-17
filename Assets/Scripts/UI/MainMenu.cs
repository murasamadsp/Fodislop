#nullable enable

using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Fodinae
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        private const string GameSceneName = "MainGame";

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

        protected void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _built = false;
            }
        }
        protected void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "MainMenu requires a UIDocument with a ready rootVisualElement.");
            }

            _root = _doc.rootVisualElement;

            if (_built && Application.isPlaying)
            {
                RebindGameManager();
                SubscribePlayButton();
                return;
            }

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

            if (_loaderImage != null && Application.isPlaying)
            {
                Texture2D texture = _loaderTexture ?? throw new InvalidOperationException(
                    "MainMenu requires an explicit loader texture assigned in the scene.");
                _loaderImage.image = texture;
            }

            _built = true;
            Debug.Log($"[MainMenu] UI BUILT: rootChildren={_root.childCount}, panel={(_doc.panelSettings != null ? _doc.panelSettings.name : "NULL")}");
        }

        protected void Update()
        {
            if (_built && _gameManager == null && ServiceLocator.IsInitialized)
            {
                RebindGameManager();
            }
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

        private void RebindGameManager()
        {
            if (!ServiceLocator.IsInitialized)
            {
                if (_gameManager != null)
                {
                    _gameManager.OnWorldLoaded -= OnWorldLoaded;
                    _gameManager = null;
                }

                return;
            }

            GameManager? current = ServiceLocator.Resolve<GameManager>();
            if (current == null)
            {
                return;
            }

            if (_gameManager != null && !ReferenceEquals(_gameManager, current))
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }

            _gameManager = current;
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
            _gameManager.OnWorldLoaded += OnWorldLoaded;
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

            SceneManager.UnloadSceneAsync(gameObject.scene).ToUniTask().Forget();
        }

        private void OnPlayButtonClicked()
        {
            Debug.Log("[MainMenu] Play button clicked");

            HideMenu();

            LoadGameSceneAsync().Forget();
        }

        private async UniTaskVoid LoadGameSceneAsync()
        {
            AsyncOperation? loadOp = SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                throw new InvalidOperationException($"Failed to start loading scene '{GameSceneName}'.");
            }

            await loadOp.ToUniTask();

            _gameManager = ServiceLocator.Resolve<GameManager>();
            if (_gameManager != null)
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
                _gameManager.OnWorldLoaded += OnWorldLoaded;
            }

            var connectionService = ServiceLocator.Resolve<IConnectionService>();
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
