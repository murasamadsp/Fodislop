#nullable enable

using System;
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
        [Header("Local Surface Assets")]
        [SerializeField]
        private Texture2D? _transitTexture;
        [SerializeField]
        private Texture2D? _perspectiveTexture;

        private WorldBackgroundSetup? _backgroundSetup;
        private bool _surfaceRendererSetup;

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveEditorSurfaceAssets();
        }

        private void ResolveEditorSurfaceAssets()
        {
            _transitTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Textures/transit.png");
            _perspectiveTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Textures/perspective.png");
        }
#endif

        protected void Awake()
        {
#if UNITY_EDITOR
            ResolveEditorSurfaceAssets();
#endif
            SetupWorldBackground();
            SetupWorldMapController();
            SetupMinimapController();
            SetupWorldAudioController();
            TrySetupSurfaceRenderer();
        }

        protected void Update()
        {
            if (_surfaceRendererSetup)
            {
                enabled = false;
                return;
            }

            TrySetupSurfaceRenderer();
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

        private void TrySetupSurfaceRenderer()
        {
            if (!Fodinae.Core.ServiceLocator.IsInitialized)
            {
                return;
            }

            if (_transitTexture == null || _perspectiveTexture == null)
            {
                throw new InvalidOperationException(
                    "SceneSetup requires serialized transit and perspective surface textures.");
            }

            SurfaceRenderer? surfaceRenderer = FindAnyObjectByType<SurfaceRenderer>();
            if (surfaceRenderer == null)
            {
                var surfaceGO = new GameObject("SurfaceRenderer");
                surfaceGO.transform.SetParent(transform);
                surfaceRenderer = surfaceGO.AddComponent<SurfaceRenderer>();
            }

            Fodinae.Core.ServiceLocator.Inject(surfaceRenderer);
            surfaceRenderer.SetLocalAssets(_transitTexture, _perspectiveTexture);
            _surfaceRendererSetup = true;
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
