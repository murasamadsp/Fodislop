using UnityEngine;
using UnityEditor;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Editor utility for setting up the world background renderer in scenes.
    /// </summary>
    public static class WorldBackgroundEditor
    {
        [MenuItem("Tools/World/Setup Background Renderer")]
        public static void SetupBackgroundRenderer()
        {
            // Check if we already have a background renderer
            var existingRenderer = Object.FindObjectOfType<WorldBackgroundRenderer>();
            var existingSetup = Object.FindObjectOfType<SceneSetup>();

            if (existingRenderer != null)
            {
                Debug.Log("WorldBackgroundRenderer already exists in the scene.");
                Selection.activeObject = existingRenderer.gameObject;
                return;
            }

            if (existingSetup != null)
            {
                Debug.Log("SceneSetup already exists in the scene.");
                Selection.activeObject = existingSetup.gameObject;
                return;
            }

            // Create the scene setup
            var setupGO = new GameObject("SceneSetup");
            var sceneSetup = setupGO.AddComponent<SceneSetup>();
            Undo.RegisterCreatedObjectUndo(setupGO, "Create SceneSetup");

            // Create the background setup
            var backgroundSetupGO = new GameObject("WorldBackgroundSetup");
            var backgroundSetup = backgroundSetupGO.AddComponent<WorldBackgroundSetup>();
            backgroundSetupGO.transform.SetParent(setupGO.transform);
            Undo.RegisterCreatedObjectUndo(backgroundSetupGO, "Create WorldBackgroundSetup");

            // Create the background renderer
            var rendererGO = new GameObject("WorldBackgroundRenderer");
            var backgroundRenderer = rendererGO.AddComponent<WorldBackgroundRenderer>();
            rendererGO.transform.SetParent(backgroundSetupGO.transform);
            Undo.RegisterCreatedObjectUndo(rendererGO, "Create WorldBackgroundRenderer");

            // Configure the renderer
            var renderer = rendererGO.GetComponent<MeshRenderer>();
            renderer.sortingOrder = -1000;
            rendererGO.transform.position = new Vector3(0, 0, -10);

            // Make objects persistent
            GameObject.DontDestroyOnLoad(setupGO);
            GameObject.DontDestroyOnLoad(backgroundSetupGO);
            GameObject.DontDestroyOnLoad(rendererGO);

            Debug.Log("World background renderer setup completed successfully!");
            Selection.activeObject = rendererGO;
        }

        [MenuItem("Tools/World/Remove Background Renderer")]
        public static void RemoveBackgroundRenderer()
        {
            var existingRenderer = Object.FindObjectOfType<WorldBackgroundRenderer>();
            var existingSetup = Object.FindObjectOfType<SceneSetup>();

            if (existingRenderer != null)
            {
                Undo.DestroyObjectImmediate(existingRenderer.gameObject);
                Debug.Log("Removed WorldBackgroundRenderer from scene.");
            }

            if (existingSetup != null)
            {
                Undo.DestroyObjectImmediate(existingSetup.gameObject);
                Debug.Log("Removed SceneSetup from scene.");
            }
        }

        [MenuItem("Tools/World/Refresh Background Renderer")]
        public static void RefreshBackgroundRenderer()
        {
            RemoveBackgroundRenderer();
            SetupBackgroundRenderer();
        }

        [MenuItem("Tools/World/Setup Background Renderer", true)]
        public static bool ValidateSetupBackgroundRenderer()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/World/Remove Background Renderer", true)]
        public static bool ValidateRemoveBackgroundRenderer()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/World/Refresh Background Renderer", true)]
        public static bool ValidateRefreshBackgroundRenderer()
        {
            return !Application.isPlaying;
        }
    }
}