#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Rendering.PostProcessing.Scopes;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [DisallowMultipleRendererFeature]
    public class PostProcessRendererFeature : ScriptableRendererFeature
    {
        public const string WorldUILayerName = ProjectRuntimeContracts.RequiredLayers.WorldUI;

        [Serializable]
        public sealed class Settings
        {
            [SerializeField]
            [Tooltip("Optional override. If empty, the feature loads Resources/Shaders/PostProcessing/PostProcess.compute.")]
            private ComputeShader? _computeShader;

            public ComputeShader? ComputeShader => _computeShader;
        }

        [SerializeField]
        private Settings _settings = new();

        private PostProcessRenderPass? _pass;
        private ScopesRenderPass? _scopesPass;
        private Camera? _mainCamera;

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;
            _scopesPass?.Dispose();
            _scopesPass = null;
            if (PostProcessRuntimeState.MainCamera == _mainCamera)
            {
                PostProcessRuntimeState.SetMainCamera(null);
            }

            _mainCamera = null;
        }

        private void EnsurePassCreated(Camera gameplayCamera)
        {
            if (_pass != null)
            {
                return;
            }

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "PostProcessRendererFeature requires PostProcess.compute; " +
                    "the renderer feature cannot be disabled silently.");
            }

            _pass = new PostProcessRenderPass(computeShader);
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);

            ComputeShader? scopesShader = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.ScopesCompute);
            if (scopesShader != null)
            {
                try
                {
                    _scopesPass = new ScopesRenderPass(scopesShader);
                    _scopesPass.ConfigureInput(ScriptableRenderPassInput.Color);
                }
                catch (Exception exception)
                {
                    _scopesPass?.Dispose();
                    _scopesPass = null;
                    Debug.LogError(
                        "[PostProcessRendererFeature] Scopes отключены, основной " +
                        $"постпроцесс продолжает работать: {exception.Message}");
                }
            }
            else
            {
                Debug.LogWarning(
                    "[PostProcessRendererFeature] Scopes.compute is missing; " +
                    "the grading scopes will remain unavailable.");
            }

            _mainCamera = gameplayCamera;
            PostProcessRuntimeState.SetMainCamera(_mainCamera);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera.targetTexture != null)
            {
                return;
            }

            Camera? targetCamera = GameplayCamera.Resolve();
            if (targetCamera != null && cameraData.camera != targetCamera)
            {
                return;
            }

            EnsurePassCreated(cameraData.camera);
            if (_pass == null)
            {
                return;
            }

            if (_mainCamera != cameraData.camera ||
                PostProcessRuntimeState.MainCamera != cameraData.camera)
            {
                _mainCamera = cameraData.camera;
                PostProcessRuntimeState.SetMainCamera(_mainCamera);
            }

            renderer.EnqueuePass(_pass);
            if (_scopesPass != null && ScopesRenderPass.Enabled)
            {
                renderer.EnqueuePass(_scopesPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
            _scopesPass?.Dispose();
            _scopesPass = null;
            if (PostProcessRuntimeState.MainCamera == _mainCamera)
            {
                PostProcessRuntimeState.SetMainCamera(null);
            }

            _mainCamera = null;
        }
    }
}
