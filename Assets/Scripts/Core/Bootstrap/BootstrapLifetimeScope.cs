#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Backend;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Core.Localization;
using Fodinae.Game.Managers;
using Fodinae.World;
using Fodinae.AssetPipeline;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Rendering;
using Fodinae.UI;
using MinesServer.Networking.Connection.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-30000)]
    public class BootstrapLifetimeScope : LifetimeScope, IMainMenuNavigation, ISceneNavigator
    {
        private const string MainMenuSceneName = "MainMenu";

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = "Gateway";

        [SerializeField] private Camera _applicationCamera = null!;
        [SerializeField] private ConnectionManager _connectionManager = null!;
        [SerializeField] private NetworkService _networkService = null!;
        [SerializeField] private AudioSystem _audioSystem = null!;
        [SerializeField] private ClientConfigManager _clientConfigManager = null!;
        [SerializeField] private ClientAssetLoader _clientAssetLoader = null!;
        [SerializeField] private TextureStorageManager _textureStorageManager = null!;
        [SerializeField] private BootstrapLoadingScreen _loadingScreen = null!;
        [SerializeField] private FMODUnity.StudioListener _studioListener = null!;

        private readonly SemaphoreSlim _transitionGate = new(1, 1);

        private string? _currentSceneName;

        public string? CurrentSceneName => _currentSceneName;

        public event Action<string>? TransitionStarted;

        public event Action<string>? TransitionCompleted;

        public event Action<string, Exception>? TransitionFailed;

        /// <summary>
        /// Stops Unity capturing a managed stack trace for plain
        /// <see cref="LogType.Log"/> messages.
        /// </summary>
        /// <remarks>
        /// The stack trace, not the message, is what makes Debug.Log expensive:
        /// Unity walks and formats the managed call stack on every single call,
        /// on the calling thread. For an informational log nobody reads the
        /// stack of, that is pure cost, and it is paid in the editor and in
        /// development builds - exactly where anyone is looking at a frame
        /// graph and wondering about unexplained spikes.
        ///
        /// Warning, Error, Assert and Exception are deliberately untouched:
        /// their stack traces are the whole point, and FailFastLogHandler acts
        /// on them.
        ///
        /// Runs before the first scene loads so no log beats it to the punch.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLogStackTraces()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        }

        protected override void Awake()
        {
            // Bootstrap only owns application services. Scene transitions are
            // started by ApplicationBootstrap after the container is built.
            DontDestroyOnLoad(gameObject);
            base.Awake();
            if (Container != null)
            {
                BindApplicationCamera();
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // Do not dispose the gate here. A transition can still be unwinding
            // after the scope receives OnDestroy (for example when the Test
            // Runner exits PlayMode). Disposing it makes the continuation's
            // finally block throw ObjectDisposedException and masks the real
            // transition result. SemaphoreSlim is managed state and can be
            // reclaimed normally once no transition references it.
            base.OnDestroy();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnforceSingleCamera(scene);
        }

        private void BindApplicationCamera()
        {
            Camera camera = _applicationCamera ?? throw new InvalidOperationException(
                "Bootstrap scene contract requires an authored application camera reference.");
            camera.enabled = true;
            camera.tag = "MainCamera";
            camera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            if (_studioListener == null || _studioListener.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException(
                    "Bootstrap scene contract requires an authored FMOD StudioListener reference in this scene.");
            }

            GameplayCamera.BindPersistent(camera);
            EnforceSingleCamera(gameObject.scene);
        }

        private void EnforceSingleCamera(Scene scene)
        {
            Camera applicationCamera = _applicationCamera;
            if (applicationCamera == null || applicationCamera.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException("BootstrapLifetimeScope requires an authored application camera reference in this scene.");
            }
            applicationCamera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            applicationCamera.clearFlags = CameraClearFlags.SolidColor;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera == applicationCamera || camera.targetTexture != null ||
                        camera.GetComponentInParent<MenuSceneryController>() != null)
                    {
                        continue;
                    }

                    camera.enabled = false;
                    camera.tag = "Untagged";
                }
            }
        }

        private async UniTask EnsureMainMenuLoadedAsync()
        {
            await TransitionAsync(MainMenuSceneName, destroyCancellationToken);
        }

        public async UniTask TransitionAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name is required.", nameof(sceneName));
            }

            bool gateEntered = false;
            await _transitionGate.WaitAsync(cancellationToken);
            gateEntered = true;
            try
            {
                await TransitionExclusiveAsync(sceneName, cancellationToken);
            }
            finally
            {
                if (gateEntered)
                {
                    _transitionGate.Release();
                }
            }
        }

        private async UniTask TransitionExclusiveAsync(
            string sceneName,
            CancellationToken cancellationToken)
        {
            Scene? previousScene = null;
            string? currentSceneName = _currentSceneName;
            if (!string.IsNullOrEmpty(currentSceneName))
            {
                // Do not use SceneManager.GetSceneByName here: it resolves by
                // an internal name->scene map that is unreliable once multiple
                // additive scenes are loaded, and can return an invalid Scene
                // even when a correctly-named scene is resident. Scan the live
                // scene list instead, matching the target-side lookup.
                Scene previous = FindFirstLoadedScene(currentSceneName);
                if (previous.IsValid() && previous.isLoaded)
                {
                    previousScene = previous;
                }
            }

            Debug.Log($"[Bootstrap] Loading scene '{sceneName}'...");
            TransitionStarted?.Invoke(sceneName);
            // The outgoing scene is no longer the current transition owner.
            // Keep it loaded while the ticketed replacement reaches presentation.
            _currentSceneName = null;
            Scene candidateScene = default;
            SceneTransitionTicket? ticket = null;
            try
            {
                if (!candidateScene.isLoaded)
                {
                    if (!Application.CanStreamedLevelBeLoaded(sceneName))
                    {
                        throw new SceneContractException(
                            $"Transition target scene '{sceneName}' is not present in the active build profile.");
                    }

                    ticket = new SceneTransitionTicket(sceneName);
                    using (LifetimeScope.EnqueueParent(this))
                    using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(ticket)))
                    {
                        await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask();
                    }

                    candidateScene = FindUniqueLoadedScene(sceneName);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"[Bootstrap] Transition target '{sceneName}' was already loaded outside the current transition. " +
                        "Content scenes must be loaded only through BootstrapLifetimeScope.");
                }

                await ticket.WaitUntilAttachedAsync().AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                SceneManager.SetActiveScene(candidateScene);
                ticket.RequestActivation();
                await ticket.WaitForPresentationAsync().AttachExternalCancellation(cancellationToken);
                _currentSceneName = sceneName;

                if (previousScene.HasValue && previousScene.Value != candidateScene &&
                    previousScene.Value.IsValid() && previousScene.Value.isLoaded)
                {
                    await PrepareSceneForUnloadAsync(previousScene.Value);
                    await SceneManager.UnloadSceneAsync(previousScene.Value).ToUniTask();
                }

                Debug.Log($"[Bootstrap] Scene '{sceneName}' entered successfully.");
                TransitionCompleted?.Invoke(sceneName);
            }
            catch (Exception ex)
            {
                ticket?.Fail(ex);
                TransitionFailed?.Invoke(sceneName, ex);
                _currentSceneName = previousScene?.name;
                if (candidateScene.IsValid() && candidateScene.isLoaded &&
                    !string.Equals(candidateScene.name, _currentSceneName, StringComparison.Ordinal))
                {
                    await SceneManager.UnloadSceneAsync(candidateScene).ToUniTask();
                }

                throw;
            }
            finally
            {
                ticket?.Dispose();
            }
        }

        private static Scene FindFirstLoadedScene(string sceneName)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default;
        }

        private static Scene FindUniqueLoadedScene(string sceneName)
        {
            Scene result = default;
            int count = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
                {
                    result = scene;
                    count++;
                }
            }

            if (count > 1)
            {
                throw new InvalidOperationException(
                    $"[Bootstrap] Transition target '{sceneName}' is loaded {count} times. Unload duplicate scene instances before continuing.");
            }

            return result;
        }

        private static GameLifetimeScope? FindGameScope(Scene scene)
        {
            GameLifetimeScope? result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (GameLifetimeScope scope in root.GetComponentsInChildren<GameLifetimeScope>(true))
                {
                    if (scope.gameObject.scene == scene)
                    {
                        if (result != null)
                        {
                            throw new InvalidOperationException(
                                $"[Bootstrap] Scene '{scene.name}' contains multiple GameLifetimeScope components. Keep exactly one composition root.");
                        }

                        result = scope;
                    }
                }
            }

            return result;
        }

        private static async UniTask PrepareSceneForUnloadAsync(Scene scene)
        {
            GameLifetimeScope? scope = FindGameScope(scene);
            if (scope == null || scope.Container == null)
            {
                return;
            }

            await scope.PrepareForUnloadAsync(
                scope.Container.Resolve<PacketHandler>(),
                scope.Container.Resolve<GameManager>(),
                scope.Container.Resolve<MapManager>(),
                scope.Container.Resolve<AsyncOperationSupervisor>());
        }


        /// <summary>
        /// Disconnects, tears down the current world, and returns to the main menu.
        /// Runs on the Bootstrap scope, which survives the whole transition — the caller
        /// (e.g. PauseMenu) lives in MainGame and gets destroyed partway through this.
        /// </summary>
        public void ReturnToMainMenu()
        {
            Container.Resolve<AsyncOperationSupervisor>().Run(
                "return_to_main_menu",
                ReturnToMainMenuAsync);
        }

        private async UniTask ReturnToMainMenuAsync(CancellationToken cancellationToken)
        {
            // Packet subscriptions come off first, while the game scope is still
            // alive and this resolve is still valid. Leaving it to
            // PacketHandler.OnDestroy means it happens inside the unload, after
            // packets have already had a chance to reach processors that resolve
            // managers out of a dying container.
            Container.Resolve<IConnectionService>().Disconnect();

            // Ambient resolution is pointed back at Bootstrap BEFORE the unload,
            // not after it.
            //
            // When the Game scope disposes, VContainer clears sharedInstances but
            // leaves the registry intact, so a Resolve on a disposed scope silently
            // re-runs the provider. For RegisterComponent registrations the provider
            // resolves the existing authored component reference — once Bootstrap
            // unloads the scene, those references point at destroyed objects.
            // Repointing first means late resolves hit the Bootstrap container, where
            // Game-scoped types are not registered, and TryResolve returns null.
            await TransitionAsync(MainMenuSceneName, cancellationToken);
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // NB: this scope is already registered by LifetimeScope.InstallTo as
            // RegisterInstance<LifetimeScope>(this).AsSelf() — an explicit
            // RegisterInstance(this) here duplicates the BootstrapLifetimeScope
            // contract and VContainer rejects the conflicting singleton.
            Camera applicationCamera = _applicationCamera;
            if (applicationCamera == null || applicationCamera.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException("BootstrapLifetimeScope requires an authored application camera reference in this scene.");
            }
            builder.RegisterComponent(applicationCamera);
            builder.Register<IMainMenuNavigation>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.Register<ISceneNavigator>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.RegisterInstance(ProjectDefaultsLoader.LoadRequired());
            builder.RegisterInstance(GraphicsQualityProfileLoader.LoadRequired());
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();

            // DummyConnection — офлайн-транспорт и точка подключения симуляторов
            // (сейчас — VK-входа). Регистрируется интерфейсами, чтобы другие
            // подсистемы могли подвязаться под него без зависимости от класса.
            builder.Register<DummyConnection>(Lifetime.Singleton).AsSelf().AsImplementedInterfaces();

            // Application-tier session state: NetworkService (Bootstrap) and
            // Game-tier processors both resolve the same local-player state.
            builder.Register<LocalPlayerState>(Lifetime.Singleton).As<ILocalPlayerState>();

            // World-load phases are published by the Game scope but consumed by
            // the MainMenu loader (sibling scope), so the relay lives here.
            builder.Register<WorldLoadProgress>(Lifetime.Singleton).As<IWorldLoadProgress>();
            builder.Register<IItemCatalog>(_ => new ItemRegistryAdapter(), Lifetime.Singleton);

            // The persistent application camera as a typed DI dependency.
            // RegisterComponent(applicationCamera) below exposes the Camera
            // itself; IGameplayCamera gives a named contract so DI components
            // do not reach into the static GameplayCamera holder.
            builder.Register<GameplayCameraService>(Lifetime.Singleton).As<IGameplayCamera>();

            RegisterAuthored(builder, _connectionManager, nameof(_connectionManager));
            RegisterAuthored(builder, _networkService, nameof(_networkService));
            RegisterAuthored(builder, _audioSystem, nameof(_audioSystem));
            RegisterAuthored(builder, _clientConfigManager, nameof(_clientConfigManager));
            RegisterAuthored(builder, _clientAssetLoader, nameof(_clientAssetLoader));
            RegisterAuthored(builder, _textureStorageManager, nameof(_textureStorageManager));
            RegisterAuthored(builder, _loadingScreen, nameof(_loadingScreen));
            builder.Register<LocalizationService>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.RegisterEntryPoint<ApplicationBootstrap>();
        }

        private static RegistrationBuilder RegisterAuthored<T>(
            IContainerBuilder builder,
            T component,
            string fieldName)
            where T : MonoBehaviour
        {
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"Bootstrap scene contract is missing authored reference '{fieldName}'.");
            }

            return builder.RegisterComponent(component).AsImplementedInterfaces();
        }
    }
}
