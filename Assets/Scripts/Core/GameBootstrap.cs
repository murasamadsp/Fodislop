#nullable enable

using System;
using Fodinae.Audio.Backend;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Networking.Connection.Client;
using Fodinae.Player.Logic;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    /// <summary>
    /// Выполняет инициализацию после полной сборки DI-графа. Поля scene-компонентов
    /// инжектируются build callback-ом в GameLifetimeScope до этой фазы.
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

        public GameBootstrap(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public void PostStart()
        {
            _resolver.Resolve<ConnectionManager>();
            var networkService = _resolver.Resolve<NetworkService>();
            networkService.EnsureConnectionSubscription();
            if (!networkService.IsConnectionSubscriptionEstablished)
            {
                throw new InvalidOperationException(
                    "NetworkService failed to subscribe to the connection packet stream.");
            }
            _resolver.Resolve<MapManager>();
            _resolver.Resolve<PacketHandler>();
            var assetLoader = _resolver.Resolve<IAssetLoader>();
            if (assetLoader is ClientAssetLoader clientAssetLoader)
            {
                clientAssetLoader.EnsureAssetSubscription();
                if (!clientAssetLoader.IsAssetSubscriptionEstablished)
                {
                    throw new InvalidOperationException(
                        "ClientAssetLoader failed to subscribe to the connection packet stream.");
                }
            }

            _resolver.Resolve<IAudioSystem>();
            _resolver.Resolve<IPlayerStats>();
            _resolver.Resolve<PlayerMovementController>();

            // UI-сервисы: создаём ПЕРЕД GameManager чтобы SetupUI находил их через
            // FindAnyObjectByType(FindObjectsInactive.Include) и не создавал дубликаты.
            _resolver.Resolve<GlobalChatUI>();
            _resolver.Resolve<FloatingChatManager>();
            _resolver.Resolve<FPSCounter>();
            _resolver.Resolve<DiagnosticRunner>();
            _resolver.Resolve<IInputBlocker>();
            _resolver.Resolve<PostProcessController>();

            var gameManager = _resolver.Resolve<GameManager>();
            _resolver.Resolve<ServerConfig>();
            var lightingEngine = _resolver.Resolve<TerrariaLightingEngine>();
            lightingEngine.EnsureInitialized();
            _resolver.Resolve<TextureStorageManager>();
            _resolver.Resolve<WorldTextureManager>();
            _resolver.Resolve<ServerAudioEventManager>();
            _resolver.Resolve<VFXPool>();
            _resolver.Resolve<PackManager>();
            _resolver.Resolve<RobotManager>();

            // UI создаётся ПОСЛЕ того как все менеджеры-синглтоны уже построены и контейнер
            // полностью собран. AddInjectedComponent -> ServiceLocator.Inject не пере-резолвит
            // ничего, что находится в процессе построения.
            gameManager.EnsureUISetup();

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<TerrainRenderer>())
            {
                terrain.EnsureSubscriptions();
            }
            ValidateStartup(_resolver);
        }

        private void ValidateStartup(IObjectResolver resolver)
        {
            var errors = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            IProjectDefaults defaults = resolver.Resolve<IProjectDefaults>();
            if (defaults.SchemaVersion != ProjectDefaults.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(defaults.ContentHash))
            {
                errors.Add("ProjectDefaults snapshot is missing or invalid");
            }

            if (resolver.Resolve<IConnectionService>() == null)
            {
                errors.Add("ConnectionManager is null after VContainer build");
            }

            if (resolver.Resolve<INetworkService>() == null)
            {
                errors.Add("NetworkService is null after VContainer build");
            }

            if (resolver.Resolve<IInputBlocker>() == null)
            {
                errors.Add("IInputBlocker is null after VContainer build — input blocking will NOT work");
            }

            if (resolver.Resolve<MapManager>() == null)
            {
                errors.Add("MapManager is null after VContainer build");
            }

            if (resolver.Resolve<IWorldDataStorage>() == null)
            {
                errors.Add("MapStorage is null after VContainer build");
            }

            if (resolver.Resolve<ITextureService>() == null)
            {
                errors.Add("WorldTextureManager is null after VContainer build");
            }

            if (resolver.Resolve<IAudioSystem>() == null)
            {
                errors.Add("AudioSystem is null after VContainer build");
            }

            if (resolver.Resolve<GameManager>() == null)
            {
                errors.Add("GameManager is null after VContainer build — UI will NOT be created");
            }

            if (resolver.Resolve<UIDocument>() == null)
            {
                errors.Add("UIDocument is null after VContainer build — UI will NOT be created");
            }

            var terrain = UnityEngine.Object.FindAnyObjectByType<TerrainRenderer>();
            if (terrain == null)
            {
                errors.Add("TerrainRenderer not found in scene — terrain mesh will NOT be rendered");
            }

            if (UnityEngine.Object.FindAnyObjectByType<SurfaceRenderer>() == null)
            {
                errors.Add("SurfaceRenderer not found — world boundary surface will NOT be rendered");
            }

            if (UnityEngine.Object.FindAnyObjectByType<MinimapController>() == null)
            {
                errors.Add("MinimapController not found — minimap will NOT be rendered");
            }

            if (UnityEngine.Object.FindAnyObjectByType<PostProcessController>() == null)
            {
                errors.Add("PostProcessController not found — screen post-processing will NOT run");
            }

            if (UnityEngine.Object.FindAnyObjectByType<TerrariaLightingEngine>() == null)
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
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.Velocity);

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
                var instance = UnityEngine.Object.FindAnyObjectByType(type, FindObjectsInactive.Include);
                if (instance == null)
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
