#nullable enable

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Backend;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-30000)]
    public class BootstrapLifetimeScope : LifetimeScope, IMainMenuNavigation
    {
        private const string MainMenuSceneName = "MainMenu";

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = "Gateway";
        private const string MainGameSceneName = "MainGame";
        private readonly HashSet<ulong> _injectedSceneHandles = [];

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
            // Fail-fast сторож вешается раньше всех систем: первая ошибка
            // (включая ошибки старта самого скоупа) останавливает приложение.
            FailFastLogHandler.EnsureRegistered();

            DontDestroyOnLoad(gameObject);
            base.Awake();
            if (Container != null)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                Container.Resolve<ISessionContainer>().Set(Container);

                // ClientConfigManager is a lazy Bootstrap-tier singleton — it only
                // exists after the first Resolve, and its Start() runs a frame later.
                // Everything that reads ClientConfig.Config (ConnectionManager,
                // PostProcessController, TerrariaLightingEngine — including scene
                // components whose Start() fires BEFORE GameBootstrap.PostStart)
                // needs it loaded by the time MainGame loads. Create and initialize
                // it here, at Bootstrap startup, so the config is ready before
                // MainMenu even loads.
                Container.Resolve<IClientConfigManager>().EnsureInitialized();
            }
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            base.OnDestroy();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _injectedSceneHandles.Remove(scene.handle.GetRawData());
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == GatewaySceneName || scene.name == MainMenuSceneName)
            {
                InjectSceneBehaviours(scene);
            }
        }


        protected void Start()
        {
            EnsureGatewayLoadedAsync().Forget();
        }

        private async UniTask EnsureGatewayLoadedAsync()
        {
            await RuntimeAssetPaths.EnsureReadyAsync();

            // Если меню уже в сцене (запуск прямо из MainMenu в редакторе),
            // ворота пропускаем — иначе они перекроют уже готовый экран.
            if (SceneManager.GetSceneByName(MainMenuSceneName).isLoaded)
            {
                await EnsureMainMenuLoadedAsync();
                return;
            }

            Scene gateway = SceneManager.GetSceneByName(GatewaySceneName);
            if (!gateway.isLoaded)
            {
                await SceneManager.LoadSceneAsync(GatewaySceneName, LoadSceneMode.Additive).ToUniTask();
                gateway = SceneManager.GetSceneByName(GatewaySceneName);
            }

            if (gateway.IsValid() && gateway.isLoaded)
            {
                SceneManager.SetActiveScene(gateway);
                InjectSceneBehaviours(gateway);
            }
        }

        private async UniTask EnsureMainMenuLoadedAsync()
        {
            Scene menuScene = SceneManager.GetSceneByName(MainMenuSceneName);
            if (!menuScene.isLoaded)
            {
                await SceneManager.LoadSceneAsync(MainMenuSceneName, LoadSceneMode.Additive).ToUniTask();
                menuScene = SceneManager.GetSceneByName(MainMenuSceneName);
            }

            if (menuScene.IsValid() && menuScene.isLoaded)
            {
                SceneManager.SetActiveScene(menuScene);
                InjectSceneBehaviours(menuScene);
            }
        }

        private void InjectSceneBehaviours(Scene scene)
        {
            if (!_injectedSceneHandles.Add(scene.handle.GetRawData()))
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    // Unity preserves missing-script slots as null entries in
                    // the component array. They are not injectable behaviours;
                    // passing one into VContainer produces a teardown-time NRE.
                    if (behaviour == null || behaviour is LifetimeScope)
                    {
                        continue;
                    }

                    Container.Inject(behaviour);
                }
            }
        }

        /// <summary>
        /// Disconnects, tears down the current world, and returns to the main menu.
        /// Runs on the Bootstrap scope, which survives the whole transition — the caller
        /// (e.g. PauseMenu) lives in MainGame and gets destroyed partway through this.
        /// </summary>
        public void ReturnToMainMenu()
        {
            ReturnToMainMenuAsync().Forget();
        }

        private async UniTaskVoid ReturnToMainMenuAsync()
        {
            // Packet subscriptions come off first, while the game scope is still
            // alive and this resolve is still valid. Leaving it to
            // PacketHandler.OnDestroy means it happens inside the unload, after
            // packets have already had a chance to reach processors that resolve
            // managers out of a dying container.
            ISessionContainer session = Container.Resolve<ISessionContainer>();
            session.TryResolve<PacketHandler>()?.Shutdown();
            Container.Resolve<IConnectionService>().Disconnect();

            // Ambient resolution is pointed back at Bootstrap BEFORE the unload,
            // not after it.
            //
            // VContainer's Container.Dispose() empties sharedInstances but sets
            // no disposed flag and leaves the registry intact, so a Resolve on a
            // disposed scope silently re-runs the provider. For everything
            // registered with RegisterComponentOnNewGameObject that provider is
            // `new GameObject(typeof(T).Name)` - which is where the
            // "PackManager / RobotManager / ServerAudioEventManager created
            // while closing the scene" warning comes from. The unload spans at
            // least a frame, ConnectionManager and its packet loop are
            // Bootstrap-tier and keep running through it, and MapManager's
            // [Inject] Construct pulls all three of those at once, so a single
            // late packet was enough to resurrect the lot into a dying scene.
            //
            // Repointing first means those late resolves hit the Bootstrap
            // container, where none of them are registered, and TryResolve
            // simply returns null.
            session.Set(Container);

            Scene mainGameScene = SceneManager.GetSceneByName(MainGameSceneName);
            if (mainGameScene.isLoaded)
            {
                await SceneManager.UnloadSceneAsync(mainGameScene)!.ToUniTask();
            }

            await EnsureMainMenuLoadedAsync();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IMainMenuNavigation>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.RegisterInstance(ProjectDefaultsLoader.LoadRequired());
            builder.RegisterInstance(GraphicsQualityProfileLoader.LoadRequired());

            builder.Register<SessionContainer>(Lifetime.Singleton).AsImplementedInterfaces();

            RegisterManager<ConnectionManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<NetworkService>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<AudioSystem>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<ClientConfigManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<ClientAssetLoader>(builder).AsImplementedInterfaces().AsSelf();
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            Scene ownScene = gameObject.scene;
            T? existing = null;
            foreach (T candidate in FindObjectsByType<T>(FindObjectsInactive.Include))
            {
                if (candidate.gameObject.scene == ownScene)
                {
                    existing = candidate;
                    break;
                }
            }

            if (existing != null)
            {
                return builder.RegisterComponent(existing);
            }

            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton)
                .UnderTransform(transform);
        }
    }
}
