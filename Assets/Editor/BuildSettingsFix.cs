#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Гарантирует порядок сцен в Build Settings: Bootstrap (index 0) → MainMenu → MainGame.
    /// MainGame грузится аддитивно по имени из MainMenu, поэтому без записи в Build Settings
    /// реальная сборка не сможет его загрузить (в редакторе это работает и без этого).
    ///
    /// CLI:
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod Fodinae.Editor.BuildSettingsFix.EnsureScenesInBuildSettings
    /// </summary>
    public static class BuildSettingsFix
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

        private static readonly string[] RequiredScenePaths =
        [
            BootstrapScenePath,
            MainMenuScenePath,
            MainGameScenePath,
        ];

        [MenuItem("Fodinae/Build/Ensure Build Settings")]
        public static void EnsureScenesInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (string path in RequiredScenePaths)
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

                if (Array.IndexOf(RequiredScenePaths, scene.path) >= 0)
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
    }
}
#endif
