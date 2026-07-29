#nullable enable

using Fodinae;
using Fodinae.Audio.Backend;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Networking.Connection.Client;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-20000)]
    public class GameLifetimeScope : LifetimeScope
    {
        private readonly System.Collections.Generic.List<string> _injectionFailures = new();

        protected override void Configure(IContainerBuilder builder)
        {
            var newStorage = new MapStorage();
            newStorage.SetAsPending();
            builder.RegisterInstance(newStorage).As<IWorldDataStorage>().AsSelf();

            // Register (не RegisterInstance): VContainer сам конструирует и инжектит [Inject]-поля.
            // RegisterInstance НЕ инжектит уже созданные вручную объекты — _networkService остаётся null.
            builder.Register<InventoryModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();

            RegisterManager<MapManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TerrainRenderer>(builder);
            RegisterManager<ClientAssetLoader>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<AudioSystem>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldTextureManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<ServerAudioEventManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<ConnectionManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<PacketHandler>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<NetworkService>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<GameManager>(builder);
            RegisterManager<VFXPool>(builder);
            RegisterManager<PackManager>(builder);
            RegisterManager<RobotManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<PlayerMovementController>(builder);
            RegisterManager<ServerConfig>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TextureStorageManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<GlobalChatUI>(builder);
            RegisterManager<UIInputManager>(builder);
            RegisterManager<FPSCounter>(builder);
            RegisterManager<FloatingChatManager>(builder);

            builder.RegisterBuildCallback(resolver =>
            {
                ServiceLocator.Initialize(resolver);

                resolver.Resolve<ConnectionManager>();
                resolver.Resolve<NetworkService>();
                resolver.Resolve<MapManager>();
                resolver.Resolve<PacketHandler>();
                resolver.Resolve<IAssetLoader>();
                resolver.Resolve<IAudioSystem>();
                resolver.Resolve<GameManager>();
                resolver.Resolve<ServerConfig>();
                resolver.Resolve<TextureStorageManager>();
                resolver.Resolve<WorldTextureManager>();
                resolver.Resolve<ServerAudioEventManager>();
                resolver.Resolve<VFXPool>();
                resolver.Resolve<PackManager>();
                resolver.Resolve<RobotManager>();
                resolver.Resolve<IPlayerStats>();
                resolver.Resolve<PlayerMovementController>();

                // UI-сервисы: явно резолвим, чтобы VContainer их инсталлировал
                // (они не существуют в сцене и создаются здесь).
                resolver.Resolve<GlobalChatUI>();
                resolver.Resolve<FPSCounter>();
                resolver.Resolve<FloatingChatManager>();
                resolver.Resolve<IInputBlocker>();

                foreach (var terrain in FindObjectsByType<TerrainRenderer>())
                {
                    if (TryInject(resolver, terrain))
                    {
                        terrain.EnsureSubscriptions();
                    }
                }

                // ВАЖНО: Include — HUD живёт под неактивным UIRoot до авторизации (GameManager.SetupUI),
                // но его [Inject]-поля должны быть заполнены до Awake компонентов.
                var allMonoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
                int injected = 0;
                foreach (var mb in allMonoBehaviours)
                {
                    if (mb == null || mb is LifetimeScope || mb is GameLifetimeScope)
                    {
                        continue;
                    }

                    var type = mb.GetType();
                    var fields = type.GetFields(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    bool hasInject = false;
                    foreach (var f in fields)
                    {
                        if (System.Attribute.IsDefined(f, typeof(VContainer.InjectAttribute)))
                        {
                            hasInject = true;
                            break;
                        }
                    }

                    if (hasInject)
                    {
                        TryInject(resolver, mb);
                        injected++;
                    }
                }

                Debug.Log($"[GameLifetimeScope] Injected {injected} scene MonoBehaviours with [Inject] fields");

                LogInjectionFailures();
                ValidateStartup();
            });
        }

        private bool TryInject(IObjectResolver resolver, object target)
        {
            try
            {
                resolver.Inject(target);
                return true;
            }
            catch (VContainerException ex)
            {
                _injectionFailures.Add($"{target.GetType().Name}: {ex.Message}");
                Debug.LogError($"[GameLifetimeScope] Injection failed for {target.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void LogInjectionFailures()
        {
            if (_injectionFailures.Count == 0)
            {
                return;
            }

            Debug.LogError(
                $"[GameLifetimeScope] {_injectionFailures.Count} injection failure(s) during startup:\n- " +
                string.Join("\n- ", _injectionFailures));
        }

        private void ValidateStartup()
        {
            var errors = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            if (ServiceLocator.Resolve<IConnectionService>() == null)
            {
                errors.Add("ConnectionManager is null after VContainer build");
            }

            if (ServiceLocator.Resolve<INetworkService>() == null)
            {
                errors.Add("NetworkService is null after VContainer build");
            }

            if (ServiceLocator.Resolve<IInputBlocker>() == null)
            {
                errors.Add("IInputBlocker is null after VContainer build — input blocking will NOT work");
            }

            if (ServiceLocator.Resolve<MapManager>() == null)
            {
                errors.Add("MapManager is null after VContainer build");
            }

            if (ServiceLocator.Resolve<IWorldDataStorage>() == null)
            {
                errors.Add("MapStorage is null after VContainer build");
            }

            if (ServiceLocator.Resolve<ITextureService>() == null)
            {
                errors.Add("WorldTextureManager is null after VContainer build");
            }

            if (ServiceLocator.Resolve<IAudioSystem>() == null)
            {
                errors.Add("AudioSystem is null after VContainer build");
            }

            if (ServiceLocator.Resolve<GameManager>() == null)
            {
                errors.Add("GameManager is null after VContainer build — UI will NOT be created");
            }

            var terrain = UnityEngine.Object.FindAnyObjectByType<TerrainRenderer>();
            if (terrain == null)
            {
                errors.Add("TerrainRenderer not found in scene — terrain mesh will NOT be rendered");
            }

            ValidateInjection(errors);

            if (errors.Count > 0)
            {
                var msg = $"[GameLifetimeScope] FATAL STARTUP FAILURE: {errors.Count} critical systems failed:\n- " + string.Join("\n- ", errors);
                Debug.LogError(msg);
                return;
            }

            if (warnings.Count > 0)
            {
                Debug.LogWarning($"[GameLifetimeScope] Startup warnings:\n- " + string.Join("\n- ", warnings));
            }

            Debug.Log("[GameLifetimeScope] Startup validation PASSED — all critical systems are alive");
        }

        private void ValidateInjection(System.Collections.Generic.List<string> errors)
        {
            var injectAttr = typeof(VContainer.InjectAttribute);
            const System.Reflection.BindingFlags bindFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var criticalTypes = new System.Type[]
            {
                typeof(PacketHandler),
                typeof(PauseMenu),
                typeof(Fodinae.UI.HUD.Player.View.PlayerHUDView),
                typeof(Fodinae.UI.HUD.Inventory.View.InventoryView),
                typeof(PlayerMovementController),
                typeof(MapManager),
                typeof(WorldTextureManager),
                typeof(ClientAssetLoader),
                typeof(AudioSystem),
                typeof(TerrainRenderer),
            };

            foreach (var type in criticalTypes)
            {
                var instance = UnityEngine.Object.FindAnyObjectByType(type);
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

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            var existing = FindAnyObjectByType<T>();
            if (existing != null)
            {
                var registration = builder.RegisterInstance(existing);
                builder.RegisterBuildCallback(resolver => TryInject(resolver, existing));
                return registration;
            }

            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton);
        }
    }
}
