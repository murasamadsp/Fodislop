#nullable enable

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor
{
    internal static class TempSaveBootstrapScene
    {
        [MenuItem("Fodinae/Temp/Save Bootstrap Scene")]
        public static void Save()
        {
            Scene bootstrap = SceneManager.GetSceneByName("Bootstrap");
            Scene mainMenu = SceneManager.GetSceneByName("MainMenu");
            if (!bootstrap.IsValid())
            {
                UnityEngine.Debug.LogError("[TempSaveBootstrapScene] Bootstrap scene is not loaded.");
                return;
            }

            EditorSceneManager.SetActiveScene(bootstrap);
            EditorSceneManager.SaveScene(bootstrap);

            if (mainMenu.IsValid())
            {
                EditorSceneManager.SetActiveScene(mainMenu);
            }

            UnityEngine.Debug.Log("[TempSaveBootstrapScene] Saved Bootstrap scene, restored MainMenu as active.");
        }
    }
}
