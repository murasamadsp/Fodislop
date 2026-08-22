#nullable enable

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor
{
    // One-shot: discards the in-memory MainMenu scene (no save — the memory
    // copy is stale and would clobber the fixed file) and reopens it from disk
    // additively. Needed because "Enter Play Mode Options: Reload Scene
    // disabled" makes play mode use whatever is in editor memory.
    internal static class ReloadMainMenuFromDisk
    {
        [MenuItem("Fodinae/Temp/Reload MainMenu From Disk")]
        public static void Run()
        {
            Scene menu = SceneManager.GetSceneByName("MainMenu");
            if (menu.isLoaded)
            {
                SceneManager.UnloadSceneAsync(menu);
            }

            EditorSceneManager.OpenScene(
                "Assets/Scenes/MainMenu.unity",
                OpenSceneMode.Additive);
            Debug.Log("[ReloadMainMenuFromDisk] MainMenu reopened from disk.");
        }
    }
}
