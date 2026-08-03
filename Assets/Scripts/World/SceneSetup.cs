#nullable enable

using Fodinae.UI;
using UnityEngine;

namespace Fodinae.World
{
    /// <summary>
    /// Scene setup manager that ensures the world background renderer is properly configured.
    /// This script should be added to a persistent GameObject in the scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Run before other scripts
    public class SceneSetup : MonoBehaviour
    {
        [Header("Surface Materials")]
        [SerializeField]
        private Material? _transitMaterial;
        [SerializeField]
        private Material? _perspectiveMaterial;

        private WorldBackgroundSetup? _backgroundSetup;

#if UNITY_EDITOR
        private void OnValidate()
        {
            _transitMaterial ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/SurfaceMaterial.mat");
            _perspectiveMaterial ??= UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/SurfaceMaterial.mat");
        }
#endif

        protected void Awake()
        {
            SetupWorldBackground();
            SetupSurfaceRenderer();
            SetupWorldMapController();
            SetupMinimapController();
            SetupWorldAudioController();
        }

        private void SetupMinimapController()
        {
            var existing = FindAnyObjectByType<MinimapController>();
            if (existing != null)
            {
                return;
            }

            var minimapGO = new GameObject("MinimapController");
            minimapGO.transform.SetParent(transform);
            minimapGO.AddComponent<MinimapController>();
        }

        private void SetupWorldAudioController()
        {
            var existing = FindAnyObjectByType<Audio.Spatial.WorldAudioController>();
            if (existing != null)
            {
                return;
            }

            var audioGO = new GameObject("WorldAudioController");
            audioGO.transform.SetParent(transform);
            audioGO.AddComponent<Audio.Spatial.WorldAudioController>();
        }

        private void SetupSurfaceRenderer()
        {
            var existing = FindAnyObjectByType<SurfaceRenderer>();
            if (existing != null)
            {
                return;
            }

            var surfaceGO = new GameObject("SurfaceRenderer");
            surfaceGO.transform.SetParent(transform);
            var surfaceRenderer = surfaceGO.AddComponent<SurfaceRenderer>();
            surfaceRenderer.SetMaterials(_transitMaterial, _perspectiveMaterial);
        }

        private void SetupWorldMapController()
        {
            var existing = FindAnyObjectByType<WorldMapController>();
            if (existing != null)
            {
                return;
            }

            var controllerGO = new GameObject("WorldMapController");
            controllerGO.transform.SetParent(transform);
            controllerGO.AddComponent<WorldMapController>();
        }

        private void SetupWorldBackground()
        {
            // Find or create the background setup component
            _backgroundSetup = FindAnyObjectByType<WorldBackgroundSetup>();

            if (_backgroundSetup == null)
            {
                // Create a new GameObject for background setup
                var setupGO = new GameObject("WorldBackgroundSetup");
                _backgroundSetup = setupGO.AddComponent<WorldBackgroundSetup>();

                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(setupGO);
                }

                Debug.Log("[SceneSetup] WorldBackgroundSetup automatically created");
            }
            else
            {
                Debug.Log("[SceneSetup] WorldBackgroundSetup already exists in scene");
            }
        }
    }
}
