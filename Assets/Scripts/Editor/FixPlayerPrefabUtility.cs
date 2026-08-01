#if UNITY_EDITOR
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
                "Assets/Scripts/Game/RobotHeadlight.cs",
            };

            foreach (var path in scriptPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.ImportAsset("Assets/Prefabs/Player.prefab", ImportAssetOptions.ForceUpdate);

            // 2. Проверка компонентов на Assets/Prefabs/Player.prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab != null)
            {
                bool prefabModified = false;

                if (prefab.GetComponent<Fodinae.Player.Logic.PlayerMovementController>() == null)
                {
                    prefab.AddComponent<Fodinae.Player.Logic.PlayerMovementController>();
                    prefabModified = true;
                }

                if (prefab.GetComponent<Fodinae.Game.Robot>() == null)
                {
                    prefab.AddComponent<Fodinae.Game.Robot>();
                    prefabModified = true;
                }

                if (prefab.GetComponent<Fodinae.Player.PlayerInteractionController>() == null)
                {
                    prefab.AddComponent<Fodinae.Player.PlayerInteractionController>();
                    prefabModified = true;
                }

                if (prefab.GetComponent<Fodinae.Player.Input.PlayerInputHandler>() == null)
                {
                    prefab.AddComponent<Fodinae.Player.Input.PlayerInputHandler>();
                    prefabModified = true;
                }

                if (prefab.GetComponent<Fodinae.Game.RobotHeadlight>() == null)
                {
                    prefab.AddComponent<Fodinae.Game.RobotHeadlight>();
                    prefabModified = true;
                }

                if (prefabModified)
                {
                    EditorUtility.SetDirty(prefab);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[FixPlayerPrefabUtility] Fixed and saved missing components on Assets/Prefabs/Player.prefab");
                }
            }

            // 3. Проверка инстанса Player в открытой сцене
            var playerSceneGo = GameObject.FindGameObjectWithTag("Player");
            if (playerSceneGo != null)
            {
                // Очистка битых скриптов (Missing Script)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(playerSceneGo);

                bool sceneModified = false;

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

                if (playerSceneGo.GetComponent<Fodinae.Game.RobotHeadlight>() == null)
                {
                    playerSceneGo.AddComponent<Fodinae.Game.RobotHeadlight>();
                    sceneModified = true;
                }

                if (sceneModified)
                {
                    EditorSceneManager.MarkSceneDirty(playerSceneGo.scene);
                    Debug.Log("[FixPlayerPrefabUtility] Fixed missing components on scene Player GameObject");
                }
            }
        }
    }
}
#endif
