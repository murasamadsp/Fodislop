#nullable enable

using System;
using Fodinae.Audio.Backend;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
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
    /// Причина существования: резолвить менеджеры и собирать UI прямо в build-callback'е
    /// (внутри Build()) опасно — лениво создаваемые через RegisterComponentOnNewGameObject
    /// singletons строятся с побочным эффектом Awake, и резолв на ещё не построенный
    /// Lazy даёт reentrancy ("ValueFactory attempted to access Value property").
    /// После завершения Build() любой резолв безопасен — весь граф уже всев.
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

            // ClientConfigManager is a lazy Bootstrap-tier singleton: the first Resolve
            // creates it, and its Start() only runs on the NEXT frame. Everything below
            // (ConnectionManager, PostProcessController, TerrariaLightingEngine, ...)
            // reads ClientConfig.Config this frame, so force it to exist and load now.
            // Without this, PostStart throws "ClientConfig is not initialized" and the
            // world starts without lighting config and post-processing.
            _resolver.Resolve<IClientConfigManager>().EnsureInitialized();

            // Managers not already present in the scene get created lazily on first
            // Resolve() below via RegisterComponentOnNewGameObject, and Unity places new
            // GameObjects into whatever scene is active. Additive loads don't switch the
            // active scene on their own, so without this, managers created here would land
            // in whatever scene loaded us (e.g. a menu) and get destroyed when it unloads.
            SceneManager.SetActiveScene(_ownScene);


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

            var audioSystem = _resolver.Resolve<AudioSystem>();
            audioSystem.ApplySavedBusVolumes();
            _resolver.Resolve<IPlayerStats>();
            _resolver.Resolve<PlayerMovementController>();
            _resolver.Resolve<CameraFollow>();
            _resolver.Resolve<TerrainRenderer>();
            _resolver.Resolve<WorldBackgroundSetup>();
            _resolver.Resolve<WorldEntityBatchRenderer>();

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

            var gameManager = _resolver.Resolve<GameManager>();
            _resolver.Resolve<ServerConfig>();
            var lightingEngine = _resolver.Resolve<TerrariaLightingEngine>();
            lightingEngine.EnsureInitialized();
            _resolver.Resolve<SurfaceRenderer>();
            _resolver.Resolve<TextureStorageManager>();
            _resolver.Resolve<WorldTextureManager>();
            _resolver.Resolve<ServerAudioEventManager>();
            _resolver.Resolve<VFXPool>();
            _resolver.Resolve<PackManager>();
            _resolver.Resolve<RobotManager>();

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

        private void InjectSceneBehaviours()
        {
            foreach (GameObject root in _ownScene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour is LifetimeScope)
                    {
                        continue;
                    }

                    _resolver.Inject(behaviour);
                }
            }
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

            if (!resolver.TryResolve<TerrariaLightingEngine>(out _))
            {
                errors.Add("TerrariaLightingEngine not found — world lighting will NOT run");
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
