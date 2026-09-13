#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class BuildSettingsFix
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string GatewayScenePath = "Assets/Scenes/Gateway.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

        private static readonly string[] _RequiredScenePaths =
        [
            BootstrapScenePath,
            GatewayScenePath,
            MainMenuScenePath,
            MainGameScenePath,
        ];

        [MenuItem("Fodinae/Build/Ensure Build Settings")]
        public static void EnsureScenesInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (string path in _RequiredScenePaths)
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"[BuildSettingsFix] Required scene is missing: {path}");
                    continue;
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            // Сохраняем любые дополнительные сцены, уже присутствующие в настройках.
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene == null || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                if (Array.IndexOf(_RequiredScenePaths, scene.path) >= 0)
                {
                    continue;
                }

                scenes.Add(scene);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            string summary = string.Join(", ", Array.ConvertAll(
                EditorBuildSettings.scenes,
                static scene => scene.path));
            Debug.Log($"[BuildSettingsFix] Build settings updated ({EditorBuildSettings.scenes.Length} scenes): {summary}");
        }

        public static void ValidateScenesInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length < _RequiredScenePaths.Length)
            {
                throw new InvalidOperationException(
                    $"Build Settings contain {scenes.Length} scene(s); " +
                    $"at least {_RequiredScenePaths.Length} production scenes are required.");
            }

            for (int index = 0; index < _RequiredScenePaths.Length; index++)
            {
                string requiredPath = _RequiredScenePaths[index];
                if (!File.Exists(requiredPath))
                {
                    throw new FileNotFoundException("Required production scene is missing.", requiredPath);
                }

                EditorBuildSettingsScene scene = scenes[index];
                if (scene == null || !scene.enabled || scene.path != requiredPath)
                {
                    string actual = scene == null
                        ? "<null>"
                        : $"{scene.path} (enabled={scene.enabled})";
                    throw new InvalidOperationException(
                        $"Build Settings scene {index} must be '{requiredPath}' and enabled; actual: {actual}. " +
                        "Run Fodinae/Build/Ensure Build Settings explicitly to migrate authoring data.");
                }
            }
        }
    }
}
#endif
