using Fodinae.Scripts;
using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Networking.Connection.Client;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Inventory.View;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.UI.HUD.Player.View;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using VContainer;
using VContainer.Unity;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private GameObject _playerPrefab;
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[GameLifetimeScope] Configure START");
            Debug.Log($"[GameLifetimeScope] MapStorage exists={(ServiceLocator.Resolve<IWorldDataStorage>() != null)}");

            var existingStorage = ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (existingStorage != null && !existingStorage.IsDisposed)
            {
                builder.RegisterInstance(existingStorage).As<IWorldDataStorage>();
            }
            else
            {
                var newStorage = new MapStorage();
                newStorage.SetAsPending();
                builder.RegisterInstance(newStorage).As<IWorldDataStorage>();
            }

            builder.RegisterInstance(new InventoryModel()).As<IInventoryModel>();
            builder.RegisterInstance(new PlayerStatsModel()).As<IPlayerStats>();

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
            RegisterManager<ServerConfig>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TextureStorageManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<GlobalChatUI>(builder);
            RegisterManager<UIInputManager>(builder);
            RegisterManager<FPSCounter>(builder);
            RegisterManager<FloatingChatManager>(builder);

            builder.RegisterBuildCallback(resolver =>
            {
                Debug.Log("[GameLifetimeScope] BuildCallback START");
                ServiceLocator.Initialize(resolver);

                resolver.Resolve<ConnectionManager>();
                Debug.Log("[GameLifetimeScope] Resolved ConnectionManager");
                resolver.Resolve<NetworkService>();
                Debug.Log("[GameLifetimeScope] Resolved NetworkService");
                resolver.Resolve<MapManager>();
                Debug.Log("[GameLifetimeScope] Resolved MapManager");
                resolver.Resolve<PacketHandler>();
                Debug.Log("[GameLifetimeScope] Resolved PacketHandler");
                resolver.Resolve<IAssetLoader>();
                Debug.Log("[GameLifetimeScope] Resolved IAssetLoader");
                resolver.Resolve<IAudioSystem>();
                Debug.Log("[GameLifetimeScope] Resolved IAudioSystem");
                resolver.Resolve<GameManager>();
                Debug.Log("[GameLifetimeScope] Resolved GameManager");
                resolver.Resolve<ServerConfig>();
                Debug.Log("[GameLifetimeScope] Resolved ServerConfig");
                resolver.Resolve<TextureStorageManager>();
                Debug.Log("[GameLifetimeScope] Resolved TextureStorageManager");
                resolver.Resolve<WorldTextureManager>();
                Debug.Log("[GameLifetimeScope] Resolved WorldTextureManager");
                resolver.Resolve<ServerAudioEventManager>();
                Debug.Log("[GameLifetimeScope] Resolved ServerAudioEventManager");
                resolver.Resolve<VFXPool>();
                Debug.Log("[GameLifetimeScope] Resolved VFXPool");
                resolver.Resolve<PackManager>();
                Debug.Log("[GameLifetimeScope] Resolved PackManager");
                resolver.Resolve<RobotManager>();
                Debug.Log("[GameLifetimeScope] Resolved RobotManager");
                resolver.Resolve<IPlayerStats>();
                Debug.Log("[GameLifetimeScope] Resolved IPlayerStats");

                foreach (var terrain in FindObjectsByType<TerrainRenderer>())
                {
                    resolver.Inject(terrain);
                    terrain.EnsureSubscriptions();
                    Debug.Log("[GameLifetimeScope] Injected and subscribed terrain: " + terrain.name);
                }

                var allMonoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
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
                        resolver.Inject(mb);
                        injected++;
                    }
                }
                Debug.Log($"[GameLifetimeScope] Injected {injected} scene MonoBehaviours with [Inject] fields");

                Debug.Log("[GameLifetimeScope] BuildCallback END");
                ValidateStartup();
            });
            Debug.Log("[GameLifetimeScope] Configure END");
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

            if ((ServiceLocator.Resolve<MapManager>()) == null)
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

            if ((ServiceLocator.Resolve<IAudioSystem>()) == null)
            {
                errors.Add("AudioSystem is null after VContainer build");
            }

            if ((ServiceLocator.Resolve<GameManager>()) == null)
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
            var bindFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var criticalTypes = new System.Type[]
            {
                typeof(PacketHandler),
                typeof(PauseMenu),
                typeof(Fodinae.Scripts.UI.HUD.Player.View.PlayerHUDView),
                typeof(Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView),
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
                builder.RegisterBuildCallback(resolver => resolver.Inject(existing));
                return registration;
            }

            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton);
        }

        private void Start()
        {
            Debug.Log("[GameLifetimeScope] Start()");

            if (FindAnyObjectByType<DiagnosticRunner>() == null)
            {
                var diagGO = new GameObject("DiagnosticRunner");
                diagGO.AddComponent<DiagnosticRunner>();
            }

            if (FindAnyObjectByType<InjectDiagnostic>() == null)
            {
                var injDiagGO = new GameObject("InjectDiagnostic");
                injDiagGO.AddComponent<InjectDiagnostic>();
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                Container.Inject(player);
            }
            else if (_playerPrefab != null)
            {
                Container.Instantiate(_playerPrefab);
            }

            var mainMenu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            if (mainMenu != null)
            {
                Container.Inject(mainMenu);
                Debug.Log("[GameLifetimeScope] Injected MainMenu");
            }

            var inventoryView = UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Inventory.View.InventoryView>();
            if (inventoryView != null)
            {
                Container.Inject(inventoryView);
                Debug.Log("[GameLifetimeScope] Injected InventoryView");
            }

            var inventoryPresenter = UnityEngine.Object.FindAnyObjectByType<Fodinae.Scripts.UI.HUD.Inventory.Presenter.InventoryPresenter>();
            if (inventoryPresenter != null)
            {
                Container.Inject(inventoryPresenter);
                Debug.Log("[GameLifetimeScope] Injected InventoryPresenter");
            }
        }
    }
}
