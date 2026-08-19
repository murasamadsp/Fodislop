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
                Container.Resolve<ISessionContainer>().Set(Container);
            }
        }


        protected void Start()
        {
            EnsureMainMenuLoadedAsync().Forget();
        }

        private async UniTask EnsureMainMenuLoadedAsync()
        {
            if (SceneManager.GetSceneByName(MainMenuSceneName).isLoaded)
            {
                return;
            }

            await SceneManager.LoadSceneAsync(MainMenuSceneName, LoadSceneMode.Additive).ToUniTask();
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
            ServiceLocator.Resolve<IConnectionService>()?.Disconnect();

            Scene mainGameScene = SceneManager.GetSceneByName(MainGameSceneName);
            if (mainGameScene.isLoaded)
            {
                await SceneManager.UnloadSceneAsync(mainGameScene)!.ToUniTask();
            }

            // MainGame's own child scope is gone with it; restore ServiceLocator to Bootstrap's
            // container so the fresh MainMenu instance (and anything else) can resolve again.
            ServiceLocator.Initialize(Container);
            Container.Resolve<ISessionContainer>().Set(Container);

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
