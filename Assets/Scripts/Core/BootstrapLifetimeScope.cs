#nullable enable

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
    public class BootstrapLifetimeScope : LifetimeScope
    {
        private const string MainMenuSceneName = "MainMenu";

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = "Gateway";
        private const string MainGameSceneName = "MainGame";

        public static BootstrapLifetimeScope? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Instance = null;
        }

        protected override void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            base.Awake();
            if (Container != null)
            {
                ServiceLocator.Initialize(Container);
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


        protected void Start()
        {
            EnsureGatewayLoadedAsync().Forget();
        }

        private async UniTask EnsureGatewayLoadedAsync()
        {
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
            ServiceLocator.Resolve<ISessionContainer>().TryResolve<PacketHandler>()?.Shutdown();

            ServiceLocator.Resolve<IConnectionService>()?.Disconnect();

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
            ServiceLocator.Initialize(Container);
            Container.Resolve<ISessionContainer>().Set(Container);

            Scene mainGameScene = SceneManager.GetSceneByName(MainGameSceneName);
            if (mainGameScene.isLoaded)
            {
                await SceneManager.UnloadSceneAsync(mainGameScene)!.ToUniTask();
            }

            await EnsureMainMenuLoadedAsync();
        }

        protected override void Configure(IContainerBuilder builder)
        {
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
