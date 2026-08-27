#nullable enable

using System;
using Fodinae.Audio.Backend;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Networking.Connection.Client;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    /// <summary>
    /// Выполняет инициализацию после полной сборки DI-графа, включая инжект
    /// [Inject]-полей существующих scene-компонентов (InjectSceneBehaviours).
    ///
    /// All managers are authored as scene objects under ServicesRoot and registered
    /// via RegisterComponent during GameLifetimeScope.Configure. PostStart merely
    /// resolves them in the deterministic order that preserves construction
    /// contracts. Running resolves in the build callback would be dangerous
    /// because the container graph isn't fully wired yet — any Resolve there
    /// could trigger reentrancy before all registrations are visible.
    /// After Build() completes, every Resolve is safe since the entire
    /// graph is already assembled.
    /// </summary>
    public sealed class GameBootstrap : IPostStartable
    {
        private readonly IObjectResolver _resolver;
        private readonly Scene _ownScene;
        private readonly ISessionContainer _session;

        public GameBootstrap(IObjectResolver resolver, Scene ownScene, ISessionContainer session)
        {
            _resolver = resolver;
            _ownScene = ownScene;
            _session = session;
        }

        public void PostStart()
        {
            _session.Set(_resolver);

            // ClientConfigManager is an authored Bootstrap-tier singleton: it exists
            // after BootstrapLifetimeScope.Awake calls EnsureInitialized, but its
            // Start() runs a frame later. Everything below (ConnectionManager,
            // PostProcessController, LightingEngine, ...) reads ClientConfig.Config
            // this frame, so force it to exist and load now. Without this, PostStart
            // throws "ClientConfig is not initialized" and the world starts without
            // lighting config and post-processing.
            _resolver.Resolve<IClientConfigManager>().EnsureInitialized();

            // All managers are authored under ServicesRoot and resolved here in a
            // deterministic order that preserves construction contracts.
            // Managers resolve to existing scene objects — no lazy creation happens.
            // SetActiveScene is still needed so that SceneObjectFactory.Create calls
            // during construction (e.g. Robot nicknames, VFX pools) land in _ownScene,
            // not whatever scene is currently active from an additive load.
            SceneManager.SetActiveScene(_ownScene);

            ContentSceneRoot? sceneRoot = FindInScene<ContentSceneRoot>();
            sceneRoot?.BindResolver(_resolver);


            // Injects [Inject] fields on pre-existing scene MonoBehaviours (e.g. CameraFollow)
            // that aren't explicitly resolved anywhere below. Runs here, not as a build
            // callback in GameLifetimeScope.Configure(), because _ownScene isn't fully loaded
            // yet at that point — SetActiveScene above already needed the same fix.
            InjectSceneBehaviours();

            _resolver.Resolve<ConnectionManager>();
            var networkService = _resolver.Resolve<NetworkService>();
            networkService.EnsureConnectionSubscription();
            if (!networkService.IsConnectionSubscriptionEstablished && Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "NetworkService failed to subscribe to the connection packet stream.");
            }

            _resolver.Resolve<MapManager>();
            var packetHandler = _resolver.Resolve<PacketHandler>();
            packetHandler.EnsureInitialized();
            var assetLoader = _resolver.Resolve<IAssetLoader>();
            if (assetLoader is ClientAssetLoader clientAssetLoader)
            {
                clientAssetLoader.EnsureAssetSubscription();
                if (!clientAssetLoader.IsAssetSubscriptionEstablished && Application.isPlaying)
                {
                    throw new InvalidOperationException(
                        "ClientAssetLoader failed to subscribe to the connection packet stream.");
                }
            }

            ResolvePlayerAndWorldServices();
            ResolveUIServices();
            GameManager gameManager = ResolveGameplayServices();

            // UI создаётся ПОСЛЕ того как все менеджеры-синглтоны уже построены и контейнер
            // полностью собран. Повторная инъекция не пере-резолвит
            // ничего, что находится в процессе построения.
            gameManager.EnsureUISetup();

            if (_resolver.TryResolve<TerrainRenderer>(out TerrainRenderer? terrainRenderer))
            {
                terrainRenderer.EnsureSubscriptions();
            }

            ValidateStartup(_resolver);
        }

        /// <summary>
        /// The order of calls in this and the neighboring Resolve phases is
        /// significant: it preserves construction dependencies between managers.
        /// Phases exist only for readability — reordering within them or
        /// swapping phase order is not permitted.
        /// </summary>
        private void ResolvePlayerAndWorldServices()
        {
            var audioSystem = _resolver.Resolve<AudioSystem>();
            audioSystem.ApplySavedBusVolumes();
            _resolver.Resolve<IPlayerStats>();
            _resolver.Resolve<PlayerMovementController>();
            _resolver.Resolve<CameraFollow>();
            _resolver.Resolve<TerrainRenderer>();
            _resolver.Resolve<WorldBackgroundSetup>();
            _resolver.Resolve<WorldEntityBatchRenderer>();
        }

        private void ResolveUIServices()
        {
            // UI-сервисы создаём до GameManager: SetupUI только активирует уже
            // зарегистрированные компоненты и не создаёт дубликаты.
            _resolver.Resolve<GlobalChatUI>();
            _resolver.Resolve<FloatingChatManager>();
            _resolver.Resolve<FPSCounter>();
            _resolver.Resolve<DiagnosticRunner>();
            _resolver.Resolve<IInputBlocker>();
            _resolver.Resolve<MinimapController>();
            _resolver.Resolve<WorldMapController>();
            _resolver.Resolve<WorldMapRenderer>();
            _resolver.Resolve<DisplayManager>();
            _resolver.Resolve<UIInputManager>();
            _resolver.Resolve<PlayerHUDView>();
            _resolver.Resolve<InventoryView>();
            _resolver.Resolve<PauseMenu>();
            _resolver.Resolve<InGameDebugOverlay>();
            _resolver.Resolve<ReconnectUI>();
            _resolver.Resolve<AssetLoadingIndicator>();
            _resolver.Resolve<MissionArrowUI>();
            PostProcessController postProcessController =
                _resolver.Resolve<PostProcessController>();
            postProcessController.EnsureVolumeSetup();
        }

        private GameManager ResolveGameplayServices()
        {
            var gameManager = _resolver.Resolve<GameManager>();
            _resolver.Resolve<ServerConfig>();
            var lightingEngine = _resolver.Resolve<LightingEngine>();
            lightingEngine.EnsureInitialized();
            _resolver.Resolve<SurfaceRenderer>();
            _resolver.Resolve<TextureStorageManager>();
            _resolver.Resolve<WorldTextureManager>();
            _resolver.Resolve<ServerAudioEventManager>();
            _resolver.Resolve<VFXPool>();
            _resolver.Resolve<BuildingManager>();
            _resolver.Resolve<RobotManager>();
            return gameManager;
        }

        private void InjectSceneBehaviours()
        {
            // Reused across roots: the array-returning overload allocates a fresh
            // MonoBehaviour[] per root, and a loaded scene has many.
            var behaviours = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (GameObject root in _ownScene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, behaviours);
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null || behaviour is LifetimeScope)
                    {
                        continue;
                    }

                    _resolver.Inject(behaviour);
                }
            }
        }

        private T? FindInScene<T>()
            where T : Component
        {
            foreach (GameObject root in _ownScene.GetRootGameObjects())
            {
                T? component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private void ValidateStartup(IObjectResolver resolver)
        {
            var errors = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            if (!resolver.TryResolve<IProjectDefaults>(out IProjectDefaults? defaults) ||
                defaults is null ||
                defaults.SchemaVersion != ProjectDefaults.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(defaults.ContentHash))
            {
                errors.Add("ProjectDefaults snapshot is missing or invalid");
            }

            if (!resolver.TryResolve<IConnectionService>(out _))
            {
                errors.Add("ConnectionManager is null after VContainer build");
            }

            if (!resolver.TryResolve<INetworkService>(out _))
            {
                errors.Add("NetworkService is null after VContainer build");
            }

            if (!resolver.TryResolve<IInputBlocker>(out _))
            {
                errors.Add("IInputBlocker is null after VContainer build — input blocking will NOT work");
            }

            if (!resolver.TryResolve<MapManager>(out _))
            {
                errors.Add("MapManager is null after VContainer build");
            }

            if (!resolver.TryResolve<IWorldDataStorage>(out _))
            {
                errors.Add("MapStorage is null after VContainer build");
            }

            if (!resolver.TryResolve<ITextureService>(out _))
            {
                errors.Add("WorldTextureManager is null after VContainer build");
            }

            if (!resolver.TryResolve<IAudioSystem>(out _))
            {
                errors.Add("AudioSystem is null after VContainer build");
            }

            if (!resolver.TryResolve<GameManager>(out _))
            {
                errors.Add("GameManager is null after VContainer build — UI will NOT be created");
            }

            if (!resolver.TryResolve<UIDocument>(out _))
            {
                errors.Add("UIDocument is null after VContainer build — UI will NOT be created");
            }

            if (!resolver.TryResolve<TerrainRenderer>(out _))
            {
                errors.Add("TerrainRenderer not found in scene — terrain mesh will NOT be rendered");
            }

            if (!resolver.TryResolve<SurfaceRenderer>(out _))
            {
                errors.Add("SurfaceRenderer not found — world boundary surface will NOT be rendered");
            }

            if (!resolver.TryResolve<MinimapController>(out _))
            {
                errors.Add("MinimapController not found — minimap will NOT be rendered");
            }

            if (!resolver.TryResolve<PostProcessController>(out _))
            {
                errors.Add("PostProcessController not found — screen post-processing will NOT run");
            }

            if (!resolver.TryResolve<LightingEngine>(out _))
            {
                errors.Add("LightingEngine not found — world lighting will NOT run");
            }

            ValidateRenderAssets(errors);

            ValidateInjection(errors);

            if (errors.Count > 0)
            {
                string msg = $"[GameBootstrap] FATAL STARTUP FAILURE: {errors.Count} critical systems failed:\n- " +
                    string.Join("\n- ", errors);
                throw new InvalidOperationException(msg);
            }

            if (warnings.Count > 0)
            {
                Debug.LogWarning($"[GameBootstrap] Startup warnings:\n- " + string.Join("\n- ", warnings));
            }

            Debug.Log("[GameBootstrap] Startup validation PASSED — all critical systems are alive");
        }

        private static void ValidateRenderAssets(System.Collections.Generic.List<string> errors)
        {
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.Terrain);
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.DynamicEmission);
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.WorldSurface);

            if (Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) == null)
            {
                errors.Add(
                    $"Required compute shader Resources/{ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute}.compute is missing");
            }
        }

        private static void ValidateShader(
            System.Collections.Generic.List<string> errors,
            string shaderName)
        {
            Shader? shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported)
            {
                errors.Add($"Required shader '{shaderName}' is missing or unsupported");
            }
        }

        private void ValidateInjection(System.Collections.Generic.List<string> errors)
        {
            var injectAttr = typeof(VContainer.InjectAttribute);
            const System.Reflection.BindingFlags bindFlags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;
            var criticalTypes = new System.Type[]
            {
                typeof(PacketHandler),
                typeof(PauseMenu),
                typeof(PlayerHUDView),
                typeof(InventoryView),
                typeof(PlayerMovementController),
                typeof(MapManager),
                typeof(WorldTextureManager),
                typeof(ClientAssetLoader),
                typeof(AudioSystem),
                typeof(TerrainRenderer),
                typeof(SurfaceRenderer),
                typeof(MinimapController),
            };

            foreach (var type in criticalTypes)
            {
                if (!_resolver.TryResolve(type, out object? instance))
                {
                    continue;
                }

                var fields = type.GetFields(bindFlags);
                foreach (var field in fields)
                {
                    if (!System.Attribute.IsDefined(field, injectAttr))
                    {
                        continue;
                    }

                    var value = field.GetValue(instance);
                    if (value == null)
                    {
                        errors.Add($"{type.Name}.{field.Name} is null — [Inject] failed (existing scene instance not injected)");
                    }
                }
            }
        }
    }
}
