#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Backend;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
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
    [RequireComponent(typeof(Camera))]
    public class BootstrapLifetimeScope : LifetimeScope, IMainMenuNavigation
    {
        private const string MainMenuSceneName = "MainMenu";

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = "Gateway";
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
            BindApplicationCamera();
            base.Awake();
            if (Container != null)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                Container.Resolve<ISessionContainer>().Set(Container);

                // ClientConfigManager is an authored Bootstrap-tier singleton — it
                // exists in the scene after BootstrapSceneAuthoring materializes it
                // under BootstrapLifetimeScope, and its Start() runs a frame later.
                // Everything that reads ClientConfig.Config (ConnectionManager,
                // PostProcessController, LightingEngine — including scene
                // components whose Start() fires BEFORE GameBootstrap.PostStart)
                // needs it loaded by the time MainGame loads. Initialize it here,
                // at Bootstrap startup, so the config is ready before MainMenu
                // even loads.
                Container.Resolve<IClientConfigManager>().EnsureInitialized();
                EnsureGatewayLoadedAsync().Forget();
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
            EnforceSingleCamera(scene);
            if (scene.name == GatewaySceneName || scene.name == MainMenuSceneName)
            {
                InjectSceneBehaviours(scene);
            }
        }

        private void BindApplicationCamera()
        {
            Camera camera = GetComponent<Camera>();
            camera.enabled = true;
            camera.tag = "MainCamera";
            camera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            if (!TryGetComponent<FMODUnity.StudioListener>(out _))
            {
                gameObject.AddComponent<FMODUnity.StudioListener>();
            }

            GameplayCamera.BindPersistent(camera);
            EnforceSingleCamera(gameObject.scene);
        }

        private void EnforceSingleCamera(Scene scene)
        {
            Camera applicationCamera = GetComponent<Camera>();
            applicationCamera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            applicationCamera.clearFlags = CameraClearFlags.SolidColor;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera == applicationCamera || camera.targetTexture != null)
                    {
                        continue;
                    }

                    camera.enabled = false;
                    camera.tag = "Untagged";
                }
            }
        }

        private async UniTask EnsureGatewayLoadedAsync()
        {
            try
            {
                await RuntimeAssetPaths.EnsureReadyAsync();
                string initialScene = SceneManager.GetSceneByName(MainMenuSceneName).isLoaded
                    ? MainMenuSceneName
                    : GatewaySceneName;
                Debug.Log($"[Bootstrap] Transitioning to initial scene '{initialScene}'...");
                await Container.Resolve<ISceneCoordinator>().TransitionAsync(
                    initialScene,
                    destroyCancellationToken);
                Debug.Log($"[Bootstrap] Transition to '{initialScene}' completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async UniTask EnsureMainMenuLoadedAsync()
        {
            await Container.Resolve<ISceneCoordinator>().TransitionAsync(
                MainMenuSceneName,
                destroyCancellationToken);
        }

        private void InjectSceneBehaviours(Scene scene)
        {
            if (!_injectedSceneHandles.Add(scene.handle.GetRawData()))
            {
                return;
            }

            // Reused across roots: the array-returning overload allocates a fresh
            // MonoBehaviour[] per root, and a loaded scene has many.
            var behaviours = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, behaviours);
                foreach (MonoBehaviour behaviour in behaviours)
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
            // When the Game scope disposes, VContainer clears sharedInstances but
            // leaves the registry intact, so a Resolve on a disposed scope silently
            // re-runs the provider. For RegisterComponent registrations the provider
            // resolves the existing authored component reference — once SceneCoordinator
            // unloads the scene, those references point at destroyed objects.
            // Repointing first means late resolves hit the Bootstrap container, where
            // Game-scoped types are not registered, and TryResolve returns null.
            session.Set(Container);

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
            builder.Register<SceneCoordinator>(Lifetime.Singleton).AsImplementedInterfaces();

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

            // Строгий контракт: сцена — единственный источник истины для
            // Bootstrap-менеджеров. Отсутствие менеджера — ошибка конфигурации,
            // а не повод создавать его в рантайме (ленивое создание прятало бы
            // дрейф сцены и плодило два источника истины). Сцена не рассинхронизируется:
            if (existing == null)
            {
                Debug.LogWarning(
                    $"[BootstrapLifetimeScope] Manager '{typeof(T).Name}' not authored in scene '{ownScene.name}'; " +
                    $"creating it on a new GameObject under BootstrapLifetimeScope. " +
                    $"Run 'Fodinae/Architecture/Materialize Bootstrap Managers' to persist it into the scene.");

                return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton)
                    .UnderTransform(transform);
            }

            if (!existing.transform.IsChildOf(transform))
            {
                existing.transform.SetParent(transform, worldPositionStays: true);
                Debug.LogWarning(
                    $"[BootstrapLifetimeScope] Manager '{typeof(T).Name}' in scene '{ownScene.name}' was not parented under BootstrapLifetimeScope; " +
                    $"reparented automatically. Run 'Fodinae/Architecture/Materialize Bootstrap Managers' to save this in the scene asset.");
            }

            return builder.RegisterComponent(existing);
        }
    }
}
