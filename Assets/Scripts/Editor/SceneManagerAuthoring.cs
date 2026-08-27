#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection.Client;
using Fodinae.Player;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor;

public static class SceneManagerAuthoring
{
    private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

    private static readonly Type[] ManagerTypes =
    [
        typeof(MapManager),
        typeof(TerrainRenderer),
        typeof(WorldBackgroundSetup),
        typeof(WorldTextureManager),
        typeof(ServerAudioEventManager),
        typeof(PacketHandler),
        typeof(GameManager),
        typeof(VFXPool),
        typeof(BuildingManager),
        typeof(RobotManager),
        typeof(WorldEntityBatchRenderer),
        typeof(ServerConfig),
        typeof(TextureStorageManager),
        typeof(GlobalChatUI),
        typeof(UIInputManager),
        typeof(FPSCounter),
        typeof(FloatingChatManager),
        typeof(ReconnectUI),
        typeof(AssetLoadingIndicator),
        typeof(MissionArrowUI),
        typeof(DiagnosticRunner),
        typeof(PostProcessController),
        typeof(LightingEngine),
        typeof(SurfaceRenderer),
        typeof(CameraFollow),
        typeof(PlayerHUDView),
        typeof(InventoryView),
        typeof(PauseMenu),
        typeof(MinimapController),
        typeof(WorldMapController),
        typeof(WorldMapRenderer),
        typeof(DisplayManager),
        typeof(InGameDebugOverlay),
    ];

    [InitializeOnLoadMethod]
    private static void RegisterSceneSelfHeal()
    {
        EditorSceneManager.sceneOpened += (scene, _) =>
        {
            if (scene.path == MainGameScenePath &&
                scene == SceneManager.GetActiveScene() &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EnsureMainGameMaterialized();
            }
        };

        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode && !Application.isBatchMode)
            {
                EnsureMainGameMaterialized();
            }
        };
    }

    [MenuItem("Fodinae/Architecture/Materialize MainGame Managers")]
    public static void MaterializeMainGameManagers()
    {
        EnsureMainGameMaterialized();
        AssetDatabase.SaveAssets();
    }

    private static void EnsureMainGameMaterialized()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool wasActive = scene.path == MainGameScenePath;
        if (!wasActive)
        {
            scene = EditorSceneManager.OpenScene(MainGameScenePath, OpenSceneMode.Additive);
        }

        try
        {
            ContentSceneRoot sceneRoot = RequireSingleSceneRoot(scene);
            Transform servicesRoot = sceneRoot.ServicesRoot;

            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (removed > 0)
                    {
                        changed = true;
                        Debug.LogWarning($"[SceneManagerAuthoring] Removed {removed} missing MonoBehaviours on '{t.name}' in '{scene.path}'.");
                    }
                }
            }

            foreach (Type managerType in ManagerTypes)
            {
                MonoBehaviour? manager = FindSingleManager(scene, managerType);
                if (manager == null)
                {
                    CreateManager(servicesRoot, managerType);
                    changed = true;
                    continue;
                }

                if (!manager.transform.IsChildOf(servicesRoot))
                {
                    manager.transform.SetParent(servicesRoot, worldPositionStays: true);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, MainGameScenePath))
            {
                throw new InvalidOperationException($"Failed to save '{MainGameScenePath}'.");
            }

            Debug.Log($"[SceneManagerAuthoring] Materialized {ManagerTypes.Length} managers under Services in '{MainGameScenePath}'.");
        }
        finally
        {
            if (!wasActive)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private static ContentSceneRoot RequireSingleSceneRoot(Scene scene)
    {
        ContentSceneRoot? result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (ContentSceneRoot candidate in root.GetComponentsInChildren<ContentSceneRoot>(true))
            {
                if (result != null)
                {
                    throw new InvalidOperationException("MainGame contains multiple ContentSceneRoot components.");
                }

                result = candidate;
            }
        }

        return result ?? throw new InvalidOperationException("MainGame has no ContentSceneRoot.");
    }

    private static MonoBehaviour? FindSingleManager(Scene scene, Type managerType)
    {
        MonoBehaviour? result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour candidate in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (candidate == null || candidate.GetType() != managerType)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"MainGame contains multiple '{managerType.FullName}' components.");
                }

                result = candidate;
            }
        }

        return result;
    }

    private static MonoBehaviour CreateManager(Transform servicesRoot, Type managerType)
    {
        var managerObject = new GameObject(managerType.Name);
        managerObject.SetActive(false);
        managerObject.transform.SetParent(servicesRoot, worldPositionStays: false);
        var manager = (MonoBehaviour)managerObject.AddComponent(managerType);
        managerObject.SetActive(true);
        return manager;
    }
}
