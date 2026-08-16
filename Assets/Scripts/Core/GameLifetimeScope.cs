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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-20000)]
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            EnsureRuntimeUiInput();

            IProjectDefaults projectDefaults = ProjectDefaultsLoader.LoadRequired();
            builder.RegisterInstance(projectDefaults);
            GraphicsQualityProfile graphicsQualityProfile =
                GraphicsQualityProfileLoader.LoadRequired();
            builder.RegisterInstance(graphicsQualityProfile);

            UIDocument? uiDocument = FindAnyObjectByType<UIDocument>(
                FindObjectsInactive.Include);
            if (uiDocument == null || uiDocument.panelSettings == null)
            {
                throw new InvalidOperationException(
                    "The scene must contain one UIDocument with PanelSettings before UI services are registered.");
            }

            builder.RegisterInstance(uiDocument);

            var newStorage = new MapStorage();
            builder.RegisterInstance(newStorage).As<IWorldDataStorage>().AsSelf();

            builder.RegisterBuildCallback(_ => ServiceLocator.Initialize(_));

            // Register (не RegisterInstance): VContainer сам конструирует и инжектит [Inject]-поля.
            // RegisterInstance НЕ инжектит уже созданные вручную объекты — _networkService остаётся null.
            builder.Register<InventoryModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<LightingGeometryRegistry>(Lifetime.Singleton);
            builder.Register<GraphicsSettingsController>(Lifetime.Singleton);

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
            RegisterManager<VFXPool>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<PackManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<RobotManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TentacleBatchRenderer>(builder);

            // PlayerMovementController живёт на PrefabInstance объекта Player (тег "Player") в сцене.
            // RegisterManager<T> через FindAnyObjectByType может не найти его надёжно до инициализации
            // сцены, что приводит к созданию нового пустого GO без Robot/SpriteRenderer/etc.
            // Поэтому регистрируем явно через тег.
            var playerGo = GameObject.FindGameObjectWithTag("Player");
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
            RegisterManager<ClientConfigManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<TextureStorageManager>(builder).AsImplementedInterfaces().AsSelf();
            RegisterManager<GlobalChatUI>(builder);
            RegisterManager<UIInputManager>(builder);
            RegisterManager<FPSCounter>(builder);
            RegisterManager<FloatingChatManager>(builder);
            RegisterManager<DiagnosticRunner>(builder);
            RegisterManager<PostProcessController>(builder);
            RegisterManager<TerrariaLightingEngine>(builder);
            RegisterManager<SurfaceRenderer>(builder);

            builder.RegisterBuildCallback(InjectSceneBehaviours);

            // Инициализация ПОСЛЕ сборки графа: резолв менеджеров, инжект scene-компонентов,
            // сборка UI, валидация. IPostStart вызывается в player-loop фазе PostStartup,
            // когда весь DI-граф уже построен — любой резолв в этот момент безопасен и
            // не вызывает reentrancy Lazy-фабрик (в отличие от build-callback'а внутри Build()).
            builder.RegisterEntryPoint<GameBootstrap>();
        }

        private static void EnsureRuntimeUiInput()
        {
            EventSystem? eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void InjectSceneBehaviours(IObjectResolver resolver)
        {
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include))
            {
                if (behaviour is LifetimeScope)
                {
                    continue;
                }

                resolver.Inject(behaviour);
            }
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder)
            where T : MonoBehaviour
        {
            var existing = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (existing != null)
            {
                return builder.RegisterComponent(existing);
            }

            return builder.RegisterComponentOnNewGameObject<T>(Lifetime.Singleton);
        }
    }
}
