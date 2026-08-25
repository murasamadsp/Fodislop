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
using Fodinae.Networking.Processors;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using global::Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-20000)]
    public class GameLifetimeScope : LifetimeScope
    {
        private Scene _ownScene;

        // Repoints ambient resolution back at the Bootstrap scope BEFORE this
        // scope's container is disposed.
        //
        // VContainer's Container.Dispose() clears sharedInstances but sets no
        // disposed flag and leaves the registry alone, so resolving a disposed
        // container silently re-runs the provider instead of failing. Everything
        // here registered with RegisterComponentOnNewGameObject has
        // `new GameObject(typeof(T).Name)` as its provider - which is exactly
        // how PackManager, RobotManager and ServerAudioEventManager came back to
        // life inside a closing scene and produced Unity's warning.
        //
        // The resolves that do it are not exotic: ConnectionManager (Bootstrap
        // tier, still running) calls TryResolve<MapManager>() from Disconnect
        // and OnDisconnected, and MapManager's [Inject] Construct takes
        // PackManager, IRobotService and IServerAudioService - so one resolve
        // spawns all three. The packet processors do the same on any late
        // packet, since the connection outlives the scene by design.
        //
        // ReturnToMainMenu repoints explicitly before its unload; this covers
        // every other way the scene can go away, including play-mode exit.
        protected override void OnDestroy()
        {
            if (Parent != null && Parent.Container != null)
            {
                if (Parent.Container.TryResolve(out ISessionContainer session))
                {
                    session.Set(Parent.Container);
                }
            }

            base.OnDestroy();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            _ownScene = gameObject.scene;

            // Additive scene loads don't switch the active scene, and managers not already
            // present in _ownScene get created via RegisterComponentOnNewGameObject — Unity
            // places new GameObjects into whatever scene is active. _ownScene isn't fully
            // loaded yet at this point (Configure runs as part of the load itself, so
            // SceneManager.SetActiveScene would throw here); GameBootstrap.PostStart applies
            // it once the scene is actually loaded and managers start getting resolved.
            builder.RegisterInstance(_ownScene);

            // IProjectDefaults/GraphicsQualityProfile are registered by BootstrapLifetimeScope
            // (parent scope) — ClientConfigManager, now Bootstrap-tier, injects them, and child
            // scopes resolve unregistered types from the parent automatically.

            UIDocument? uiDocument = null;
            foreach (UIDocument candidate in FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include))
            {
                if (candidate.gameObject.scene == _ownScene)
                {
                    uiDocument = candidate;
                    break;
                }
            }

            if (uiDocument == null || uiDocument.panelSettings == null)
            {
                throw new InvalidOperationException(
                    "The scene must contain one UIDocument with PanelSettings before UI services are registered.");
            }

            builder.RegisterInstance(uiDocument);

            var newStorage = new MapStorage();
            builder.RegisterInstance(newStorage).As<IWorldDataStorage>().AsSelf();

            builder.RegisterBuildCallback(container => container.Resolve<ISessionContainer>().Set(container));

            // Register (не RegisterInstance): VContainer сам конструирует и инжектит [Inject]-поля.
            // RegisterInstance НЕ инжектит уже созданные вручную объекты — _networkService остаётся null.
            builder.Register<InventoryModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<LightingGeometryRegistry>(Lifetime.Singleton);
            builder.Register<GraphicsSettingsController>(Lifetime.Singleton);

            RegisterManager<MapManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TerrainRenderer>(builder);
            RegisterManager<WorldBackgroundSetup>(builder);
            RegisterManager<WorldTextureManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<ServerAudioEventManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<PacketHandler>(builder).AsImplementedInterfaces().AsSelf();

            // PacketHandler инжектит процессоры по конкретному типу ([Inject] PlayerStatsProcessor и т.д.),
            // поэтому регистрируем и интерфейсы (для коллекций IPacketProcessor<...>), и сам тип (AsSelf).
            builder.Register<ClanProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<InventoryProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<StatusProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<WorldInitProcessor>(Lifetime.Singleton);
            builder.Register<RobotInfoProcessor>(Lifetime.Singleton);
            builder.Register<MapRegionProcessor>(Lifetime.Singleton);
            builder.Register<AudioPacketProcessor>(Lifetime.Singleton);
            builder.Register<PlayerInfoProcessor>(Lifetime.Singleton);
            builder.Register<RobotPositionProcessor>(Lifetime.Singleton);
            builder.Register<ChatProcessor>(Lifetime.Singleton);
            builder.Register<MissionProcessor>(Lifetime.Singleton);
            builder.Register<PackProcessor>(Lifetime.Singleton);
            builder.Register<ConnectionProcessor>(Lifetime.Singleton);
            builder.Register<MissionArrowProcessor>(Lifetime.Singleton);
            builder.Register<WindowPacketProcessor>(Lifetime.Singleton);
            RegisterManager<GameManager>(builder);
            RegisterManager<VFXPool>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<PackManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<RobotManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldEntityBatchRenderer>(builder);

            // PlayerMovementController живёт на PrefabInstance объекта Player (тег "Player") в сцене.
            // RegisterManager<T> через FindAnyObjectByType может не найти его надёжно до инициализации
            // сцены, что приводит к созданию нового пустого GO без Robot/SpriteRenderer/etc.
            // Поэтому регистрируем явно через тег. Scoped to _ownScene: FindGameObjectWithTag
            // searches every loaded scene, and during an additive load MainMenu/Bootstrap are
            // also loaded, so an unscoped lookup could bind the wrong scene's Player object.
            GameObject? playerGo = null;
            foreach (GameObject candidate in GameObject.FindGameObjectsWithTag("Player"))
            {
                if (candidate.scene == _ownScene)
                {
                    playerGo = candidate;
                    break;
                }
            }

            var existingPmc = playerGo != null ? playerGo.GetComponent<PlayerMovementController>() : null;
            if (existingPmc != null)
            {
                builder.RegisterComponent(existingPmc);
            }
            else
            {
                throw new InvalidOperationException(
                    "The scene must contain a Player object tagged 'Player' with PlayerMovementController.");
            }

            RegisterManager<ServerConfig>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TextureStorageManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<GlobalChatUI>(builder);
            RegisterManager<UIInputManager>(builder);
            RegisterManager<FPSCounter>(builder);
            RegisterManager<FloatingChatManager>(builder);
            RegisterManager<ReconnectUI>(builder);
            RegisterManager<AssetLoadingIndicator>(builder);
            RegisterManager<MissionArrowUI>(builder);
            RegisterManager<DiagnosticRunner>(builder);
            RegisterManager<PostProcessController>(builder);
            RegisterManager<TerrariaLightingEngine>(builder);
            RegisterManager<SurfaceRenderer>(builder);
            RegisterManager<CameraFollow>(builder);
            RegisterManager<ProgrammatorGrid>(builder);
            RegisterManager<PlayerHUDView>(builder);
            RegisterManager<InventoryView>(builder);
            RegisterManager<PauseMenu>(builder);
            RegisterManager<MinimapController>(builder);
            RegisterManager<WorldMapController>(builder);
            RegisterManager<WorldMapRenderer>(builder);
            RegisterManager<DisplayManager>(builder);
            RegisterManager<InGameDebugOverlay>(builder);
            builder.Register<LocalizationService>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();




            // Инициализация ПОСЛЕ сборки графа: резолв менеджеров, инжект scene-компонентов,
            // сборка UI, валидация. IPostStart вызывается в player-loop фазе PostStartup,
            // когда весь DI-граф уже построен — любой резолв в этот момент безопасен и
            // не вызывает reentrancy Lazy-фабрик (в отличие от build-callback'а внутри Build()).
            builder.RegisterEntryPoint<GameBootstrap>();
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            T? existing = null;
            foreach (T candidate in FindObjectsByType<T>(FindObjectsInactive.Include))
            {
                if (candidate.gameObject.scene == _ownScene)
                {
                    existing = candidate;
                    break;
                }
            }

            if (existing != null)
            {
                return builder.RegisterComponent(existing);
            }

            // Не создаём менеджер вручную через AddComponent прямо здесь: Configure
            // выполняется ДО сборки контейнера, а AddComponent мгновенно дёргает
            // Awake/OnEnable менеджера — в этот момент текущий контейнер ещё указывает
            // на Bootstrap-скоуп, сцена не активна, а [Inject]-поля не заполнены. Отсюда
            // весь класс багов "резолв из Awake во время Configure" (FPSCounter,
            // TerrainRenderer-камера, PauseMenu и т.п.).
            //
            // RegisterComponentOnNewGameObject делегирует создание NewGameObjectProvider:
            // неактивный GO -> AddComponent (Awake не вызывается) -> инъекция -> активация.
            // Происходит это при первом резолве — в GameBootstrap.PostStart, когда граф
            // построен, текущий контейнер указывает на игровой скоуп, а сцена уже активна.
            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton)
                .UnderTransform(transform);
        }
    }
}
