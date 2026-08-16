#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
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
        private WorldBackgroundSetup? _backgroundSetup;
        private bool _surfaceRendererSetup;
        private bool _surfaceRendererSetupStarted;

        protected void Awake()
        {
            SetupWorldBackground();
            SetupWorldMapController();
            SetupMinimapController();
            SetupWorldAudioController();
            TryStartSurfaceRendererSetup();
        }

        protected void Update()
        {
            if (_surfaceRendererSetup)
            {
                enabled = false;
                return;
            }

            TryStartSurfaceRendererSetup();
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

        private void TryStartSurfaceRendererSetup()
        {
            if (_surfaceRendererSetupStarted || !Fodinae.Core.ServiceLocator.IsInitialized)
            {
                return;
            }

            _surfaceRendererSetupStarted = true;
            SetupSurfaceRendererAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTask SetupSurfaceRendererAsync(CancellationToken cancellationToken)
        {
            try
            {
                ITextureStorageService textureStorage =
                    Fodinae.Core.ServiceLocator.Resolve<ITextureStorageService>() ??
                    throw new InvalidOperationException(
                        "SceneSetup requires ITextureStorageService for local surface assets.");
                Texture2D transitTexture = await textureStorage.GetTextureAsync(
                    "transit.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'transit.png' could not be decoded.");
                Texture2D perspectiveTexture = await textureStorage.GetTextureAsync(
                    "perspective.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'perspective.png' could not be decoded.");
                Texture2D redRockTexture = await textureStorage.GetTextureAsync(
                    "Cells/117.png",
                    cancellationToken) ??
                    throw new InvalidOperationException(
                        "Required local surface texture 'Cells/117.png' could not be decoded.");
                cancellationToken.ThrowIfCancellationRequested();

                RuntimeTextureFactory.ApplySampling(
                    transitTexture,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);
                RuntimeTextureFactory.ApplySampling(
                    perspectiveTexture,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp);
                RuntimeTextureFactory.ApplySampling(
                    redRockTexture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                SurfaceRenderer surfaceRenderer =
                    Fodinae.Core.ServiceLocator.Resolve<SurfaceRenderer>() ??
                    throw new InvalidOperationException(
                        "SceneSetup requires the registered SurfaceRenderer.");
                surfaceRenderer.SetLocalAssets(
                    transitTexture,
                    perspectiveTexture,
                    redRockTexture);
                _surfaceRendererSetup = true;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the expected teardown path during a domain reload.
            }
            finally
            {
                // Domain reload cancels the task while preserving this component.
                // Never leave the guard latched, otherwise the surface is lost forever.
                _surfaceRendererSetupStarted = false;
            }
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
