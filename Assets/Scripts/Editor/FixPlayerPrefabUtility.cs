#nullable enable

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class FixPlayerPrefabUtility
    {
        // Вызывается ТОЛЬКО вручную из меню, чтобы не вызывать бесконечный цикл Domain Reload при старте.
        [MenuItem("Fodinae/Fix Player Prefab & Instance")]
        public static void FixPlayer()
        {
            // 1. Принудительный реимпорт C# скриптов игроков для обновления кэша MonoImporter
            string[] scriptPaths = new string[]
            {
                "Assets/Scripts/Player/Logic/PlayerMovementController.cs",
                "Assets/Scripts/Game/Robot.cs",
                "Assets/Scripts/Player/PlayerInteractionController.cs",
                "Assets/Scripts/Player/Input/PlayerInputHandler.cs",
            };

            foreach (var path in scriptPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            // 2. Проверка компонентов на Assets/Prefabs/Player.prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab != null)
            {
                GameObject prefabContents = PrefabUtility.LoadPrefabContents(
                    "Assets/Prefabs/Player.prefab");
                bool prefabModified = RemoveMissingScriptsFromHierarchy(prefabContents);

                if (prefabContents.GetComponent<Fodinae.Player.Logic.PlayerMovementController>() == null)
                {
                    prefabContents.AddComponent<Fodinae.Player.Logic.PlayerMovementController>();
                    prefabModified = true;
                }

                if (prefabContents.GetComponent<Fodinae.Game.Robot>() == null)
                {
                    prefabContents.AddComponent<Fodinae.Game.Robot>();
                    prefabModified = true;
                }

                if (prefabContents.GetComponent<Fodinae.Game.Robot>() == null)
                {
                    throw new InvalidOperationException("Player prefab Robot component is missing.");
                }

                if (prefabContents.GetComponent<Fodinae.Player.PlayerInteractionController>() == null)
                {
                    prefabContents.AddComponent<Fodinae.Player.PlayerInteractionController>();
                    prefabModified = true;
                }

                if (prefabContents.GetComponent<Fodinae.Player.Input.PlayerInputHandler>() == null)
                {
                    prefabContents.AddComponent<Fodinae.Player.Input.PlayerInputHandler>();
                    prefabModified = true;
                }

                // Always round-trip through the Unity prefab API. This also
                // removes serialized fields that no longer belong to Robot.
                PrefabUtility.SaveAsPrefabAsset(prefabContents, "Assets/Prefabs/Player.prefab");
                if (prefabModified)
                {
                    Debug.Log("[FixPlayerPrefabUtility] Fixed and saved missing components on Assets/Prefabs/Player.prefab");
                }

                if (CountMissingScriptsInHierarchy(prefabContents) != 0)
                {
                    throw new InvalidOperationException(
                        "Player prefab still contains missing MonoBehaviour scripts after cleanup.");
                }

                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            // 3. Проверка инстанса Player в открытой сцене
            GameObject? playerSceneGo = FindPlayerInOpenScenes();
            if (playerSceneGo != null)
            {
                bool sceneModified = RemoveMissingScriptsFromHierarchy(playerSceneGo);

                if (playerSceneGo.GetComponent<Fodinae.Player.Logic.PlayerMovementController>() == null)
                {
                    playerSceneGo.AddComponent<Fodinae.Player.Logic.PlayerMovementController>();
                    sceneModified = true;
                }

                if (playerSceneGo.GetComponent<Fodinae.Game.Robot>() == null)
                {
                    playerSceneGo.AddComponent<Fodinae.Game.Robot>();
                    sceneModified = true;
                }

                if (playerSceneGo.GetComponent<Fodinae.Player.PlayerInteractionController>() == null)
                {
                    playerSceneGo.AddComponent<Fodinae.Player.PlayerInteractionController>();
                    sceneModified = true;
                }

                if (playerSceneGo.GetComponent<Fodinae.Player.Input.PlayerInputHandler>() == null)
                {
                    playerSceneGo.AddComponent<Fodinae.Player.Input.PlayerInputHandler>();
                    sceneModified = true;
                }

                // Always save through the Unity scene API so removed
                // serialized fields are stripped from the scene object.
                EditorUtility.SetDirty(playerSceneGo);
                EditorSceneManager.MarkSceneDirty(playerSceneGo.scene);
                if (!EditorSceneManager.SaveScene(playerSceneGo.scene))
                {
                    throw new InvalidOperationException(
                        $"Could not save scene '{playerSceneGo.scene.path}'.");
                }

                if (sceneModified)
                {
                    Debug.Log("[FixPlayerPrefabUtility] Fixed missing components on scene Player GameObject");
                }

                if (CountMissingScriptsInHierarchy(playerSceneGo) != 0)
                {
                    throw new InvalidOperationException(
                        "Scene Player still contains missing MonoBehaviour scripts after cleanup.");
                }
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Fodinae/Reset Robot Inspector Defaults")]
        public static void ResetRobotInspectorDefaults()
        {
            GameObject prefabContents = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            try
            {
                Fodinae.Game.Robot prefabRobot = prefabContents.GetComponent<Fodinae.Game.Robot>() ??
                    throw new InvalidOperationException("Player prefab Robot component is missing.");
                ApplyRobotDefaults(prefabRobot);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, "Assets/Prefabs/Player.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            GameObject? scenePlayer = FindPlayerInOpenScenes();
            if (scenePlayer != null)
            {
                Fodinae.Game.Robot sceneRobot = scenePlayer.GetComponent<Fodinae.Game.Robot>() ??
                    throw new InvalidOperationException("Scene Player Robot component is missing.");
                ApplyRobotDefaults(sceneRobot);
                EditorUtility.SetDirty(sceneRobot);
                EditorSceneManager.MarkSceneDirty(scenePlayer.scene);
                if (!EditorSceneManager.SaveScene(scenePlayer.scene))
                {
                    throw new InvalidOperationException(
                        $"Could not save scene '{scenePlayer.scene.path}'.");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[FixPlayerPrefabUtility] Reset Robot Inspector defaults.");
        }

        private static void ApplyRobotDefaults(Fodinae.Game.Robot robot)
        {
            SerializedObject serializedRobot = new(robot);
            SerializedProperty emitsDynamicLight = serializedRobot.FindProperty("_emitsDynamicLight") ??
                throw new InvalidOperationException("Robot is missing serialized field '_emitsDynamicLight'.");
            SerializedProperty intensity = serializedRobot.FindProperty("_dynamicLightIntensity") ??
                throw new InvalidOperationException("Robot is missing serialized field '_dynamicLightIntensity'.");
            SerializedProperty color = serializedRobot.FindProperty("_dynamicLightColor") ??
                throw new InvalidOperationException("Robot is missing serialized field '_dynamicLightColor'.");
            emitsDynamicLight.boolValue = true;
            intensity.floatValue = Fodinae.World.Lighting.LightingDefaults.DynamicLightIntensity;
            color.colorValue = Fodinae.World.Lighting.LightingDefaults.DynamicLightColor;
            serializedRobot.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(robot);
        }

        private static bool RemoveMissingScriptsFromHierarchy(GameObject root)
        {
            bool modified = false;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform transform in transforms)
            {
                modified |= GameObjectUtility.RemoveMonoBehavioursWithMissingScript(
                    transform.gameObject) > 0;
            }

            return modified;
        }

        private static int CountMissingScriptsInHierarchy(GameObject root)
        {
            int missingCount = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform transform in transforms)
            {
                MonoBehaviour[] components = transform.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour component in components)
                {
                    if (component == null)
                    {
                        missingCount++;
                    }
                }
            }

            return missingCount;
        }

        private static GameObject? FindPlayerInOpenScenes()
        {
            Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include);
            foreach (Transform transform in transforms)
            {
                if (transform.CompareTag("Player"))
                {
                    return transform.gameObject;
                }
            }

            return null;
        }
    }
}
#endif
