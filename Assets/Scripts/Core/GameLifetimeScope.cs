#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio.Backend;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
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
using UnityEngine.Rendering;
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
        private List<MonoBehaviour>? _ownSceneBehaviours;
        private ContentSceneRoot _sceneRoot = null!;

         // Repoints ambient resolution back at the Bootstrap scope BEFORE this
         // scope's container is disposed.
         //
         // VContainer's Container.Dispose() clears sharedInstances but sets no
         // disposed flag and leaves the registry intact, so resolving a disposed
         // scope silently re-runs the provider instead of failing. Managers here
         // are registered with RegisterComponent (not RegisterComponentOnNewGameObject),
         // so the provider resolves the existing authored component reference rather
         // than spinning up a new GameObject. Once SceneCoordinator unloads the scene,
         // those component references point at destroyed objects, and the session
         // container has already been repointed to Bootstrap via ReturnToMainMenu
         // or GameLifetimeScope.OnDestroy itself, so late resolves hit the Bootstrap
         // container — where Game-scoped types are not registered — and TryResolve
         // simply returns null.
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
            _sceneRoot = FindInOwnScene<ContentSceneRoot>() ?? throw new InvalidOperationException(
                $"Scene '{_ownScene.name}' must contain one authored ContentSceneRoot.");

            // Additive scene loads don't switch the active scene, and all managers
            // must already be authored under ServicesRoot in _ownScene — RegisterManager
            // fails fast if any are missing. _ownScene isn't fully loaded at this
            // point (Configure runs as part of the load itself), so scene-relative
            // operations are limited here; GameBootstrap.PostStart applies
            // SetActiveScene and resolves managers once the scene is actually loaded.
            builder.RegisterInstance(_ownScene);
            builder.RegisterComponent(_sceneRoot);
            builder.Register<SceneObjectFactory>(Lifetime.Singleton).AsImplementedInterfaces();

            // IProjectDefaults/GraphicsQualityProfile are registered by BootstrapLifetimeScope
            // (parent scope) — ClientConfigManager, now Bootstrap-tier, injects them, and child
            // scopes resolve unregistered types from the parent automatically.

            UIDocument? uiDocument = FindInOwnScene<UIDocument>();

            if (uiDocument == null || uiDocument.panelSettings == null)
            {
                throw new InvalidOperationException(
                    "The scene must contain one UIDocument with PanelSettings before UI services are registered.");
            }

            builder.RegisterInstance(uiDocument);

            var newStorage = new MapStorage();
            builder.RegisterInstance(newStorage).As<IWorldDataStorage>().AsSelf();

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
            builder.Register<BuildingProcessor>(Lifetime.Singleton);
            builder.Register<ConnectionProcessor>(Lifetime.Singleton);
            builder.Register<MissionArrowProcessor>(Lifetime.Singleton);
            builder.Register<WindowPacketProcessor>(Lifetime.Singleton);
            RegisterManager<GameManager>(builder);
            RegisterManager<VFXPool>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<BuildingManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<RobotManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldEntityBatchRenderer>(builder);

            // PlayerMovementController живёт на PrefabInstance объекта Player (тег "Player") в сцене.
            // RegisterManager<T> через FindAnyObjectByType может не найти его надёжно до инициализации
            // сцены, что приводит к созданию нового пустого GO без Robot/SpriteRenderer/etc.
            // Поэтому регистрируем явно через тег. Scoped to _ownScene: FindGameObjectWithTag
            // searches every loaded scene, and during an additive load MainMenu/Bootstrap are
            // also loaded, so an unscoped lookup could bind the wrong scene's Player object.
            var existingPmc = FindInOwnScene<PlayerMovementController>();
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
            Volume? postProcessVolume = FindInOwnScene<Volume>();

            if (postProcessVolume == null)
            {
                throw new InvalidOperationException(
                    "The scene must contain a Volume for PostProcessController.");
            }

            builder.RegisterComponent(postProcessVolume);
            RegisterManager<PostProcessController>(builder);
            RegisterManager<LightingEngine>(builder);
            RegisterManager<SurfaceRenderer>(builder);
            RegisterManager<CameraFollow>(builder);
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

            // The scan is only valid for this pass: a later Configure (domain
            // reload, scope rebuild) must see the scene as it is then.
            _ownSceneBehaviours = null;
        }

        /// <summary>
        /// Own-scene MonoBehaviours, scanned once per Configure. RegisterManager
        /// is called for every manager type (35 of them), and a per-type
        /// FindObjectsByType meant 35 full scene sweeps for one container build.
        /// Scanning MonoBehaviour once and filtering with `is T` yields the same
        /// candidates, since FindObjectsByType already matches by assignability.
        /// </summary>
        private List<MonoBehaviour> OwnSceneBehaviours()
        {
            if (_ownSceneBehaviours != null)
            {
                return _ownSceneBehaviours;
            }

            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            var owned = new List<MonoBehaviour>(all.Length);
            foreach (MonoBehaviour behaviour in all)
            {
                // Unity keeps missing-script slots as null entries.
                if (behaviour != null && behaviour.gameObject.scene == _ownScene)
                {
                    owned.Add(behaviour);
                }
            }

            _ownSceneBehaviours = owned;
            return owned;
        }

        private T? FindInOwnScene<T>()
            where T : MonoBehaviour
        {
            foreach (MonoBehaviour candidate in OwnSceneBehaviours())
            {
                if (candidate is T typed)
                {
                    return typed;
                }
            }

            return null;
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            T? existing = FindInOwnScene<T>();

            if (existing == null)
            {
                Debug.LogWarning(
                    $"[GameLifetimeScope] Manager '{typeof(T).Name}' not authored in scene '{_ownScene.name}'; " +
                    $"creating it on a new GameObject under ServicesRoot. " +
                    $"Run 'Fodinae/Architecture/Materialize MainGame Managers' to persist it into the scene.");

                return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton)
                    .UnderTransform(_sceneRoot.ServicesRoot);
            }

            if (!existing.transform.IsChildOf(_sceneRoot.ServicesRoot))
            {
                existing.transform.SetParent(_sceneRoot.ServicesRoot, worldPositionStays: true);
                Debug.LogWarning(
                    $"[GameLifetimeScope] Manager '{typeof(T).Name}' in scene '{_ownScene.name}' was not parented under ServicesRoot; " +
                    $"reparented automatically. Run 'Fodinae/Architecture/Materialize MainGame Managers' to save this in the scene asset.");
            }

            return builder.RegisterComponent(existing);
        }
    }
}
