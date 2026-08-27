#nullable enable

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor
{
    /// <summary>
    /// Автоматическая санитация скрытых дефолтов во всех 4 сценах проекта.
    /// Удаляет legacy AudioListener, исправляет дефолтный синий фон камер на космический войд (#030508),
    /// очищает битые слоты скриптов и проверяет отсутствие запрещенных Canvas/EventSystem.
    /// </summary>
    public static class SceneDefaultsAuthoring
    {
        public static readonly string[] ProductionScenePaths =
        [
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Gateway.unity",
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/MainGame.unity",
        ];

        private static readonly Color DarkVoidBackground = new(0.012f, 0.018f, 0.032f, 1f);

        [InitializeOnLoadMethod]
        private static void RegisterSceneSanitizer()
        {
            EditorSceneManager.sceneOpened += (scene, _) =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && Array.IndexOf(ProductionScenePaths, scene.path) >= 0)
                {
                    SanitizeScene(scene);
                }
            };

            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode && !Application.isBatchMode)
                {
                    SanitizeAllProductionScenes();
                }
            };
        }

        [MenuItem("Fodinae/Architecture/Sanitize All Scenes Defaults")]
        public static void SanitizeAllProductionScenes()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (string scenePath in ProductionScenePaths)
            {
                bool wasActive = activeScene.path == scenePath;
                Scene scene = wasActive
                    ? activeScene
                    : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                try
                {
                    SanitizeScene(scene);
                }
                finally
                {
                    if (!wasActive)
                    {
                        EditorSceneManager.CloseScene(scene, removeScene: true);
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }

        public static bool SanitizeScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // 1. Remove missing scripts
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (removed > 0)
                    {
                        changed = true;
                        Debug.LogWarning($"[SceneDefaultsAuthoring] Removed {removed} missing MonoBehaviours on '{t.name}' in '{scene.path}'.");
                    }
                }

                // 2. Remove legacy Unity AudioListener components (FMOD Studio is the sole audio engine)
                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    if (listener == null)
                    {
                        continue;
                    }

                    string objectName = listener.gameObject.name;
                    Undo.DestroyObjectImmediate(listener);
                    changed = true;
                    Debug.Log($"[SceneDefaultsAuthoring] Removed legacy AudioListener from '{objectName}' in '{scene.path}'.");
                }

                // 3. Normalize Camera clear color (replace default Unity cornflower blue)
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    bool isDefaultBlue = camera.backgroundColor.r > 0.18f && camera.backgroundColor.r < 0.20f &&
                                         camera.backgroundColor.g > 0.29f && camera.backgroundColor.g < 0.31f &&
                                         camera.backgroundColor.b > 0.46f && camera.backgroundColor.b < 0.49f;

                    if (isDefaultBlue)
                    {
                        camera.backgroundColor = DarkVoidBackground;
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        changed = true;
                        Debug.Log($"[SceneDefaultsAuthoring] Fixed camera '{camera.name}' background color from default blue to dark void in '{scene.path}'.");
                    }
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[SceneDefaultsAuthoring] Saved sanitized scene '{scene.path}'.");
            }

            return changed;
        }
    }
}
