#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Гарантирует порядок сцен в Build Settings:
    /// Bootstrap (index 0) → Gateway → MainMenu → MainGame.
    ///
    /// Все сцены, кроме Bootstrap, грузятся аддитивно ПО ИМЕНИ. В редакторе это
    /// работает и без Build Settings, а в реальной сборке — нет: сцены, которой
    /// нет в списке, для SceneManager не существует. Поэтому пропуск любой из
    /// них ломается только в собранном билде и незаметен при разработке.
    ///
    /// CLI:
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod Fodinae.Editor.BuildSettingsFix.EnsureScenesInBuildSettings
    /// </summary>
    public static class BuildSettingsFix
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string GatewayScenePath = "Assets/Scenes/Gateway.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";

        /// <summary>Порядок здесь = порядок прохождения игроком.</summary>
        private static readonly string[] RequiredScenePaths =
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

        public static void ValidateScenesInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length < RequiredScenePaths.Length)
            {
                throw new InvalidOperationException(
                    $"Build Settings contain {scenes.Length} scene(s); " +
                    $"at least {RequiredScenePaths.Length} production scenes are required.");
            }

            for (int index = 0; index < RequiredScenePaths.Length; index++)
            {
                string requiredPath = RequiredScenePaths[index];
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
