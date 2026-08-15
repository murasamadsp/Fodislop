#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [DisallowMultipleRendererFeature]
    public class PostProcessRendererFeature : ScriptableRendererFeature
    {
        public const string WorldUiLayerName = "UI";

        [Serializable]
        public sealed class Settings
        {
            [SerializeField]
            [Tooltip("Optional override. If empty, the feature loads Resources/Shaders/PostProcessing/PostProcess.compute.")]
            private ComputeShader? _computeShader;

            [SerializeField]
            private bool _runInSceneView = true;

            [SerializeField]
            private bool _runInPreviewCameras;

            public ComputeShader? ComputeShader => _computeShader;
            public bool RunInSceneView => _runInSceneView;
            public bool RunInPreviewCameras => _runInPreviewCameras;
        }

        [SerializeField]
        private Settings _settings = new();

        private PostProcessRenderPass? _pass;
        private Camera? _mainCamera;

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "PostProcessRendererFeature requires PostProcess.compute; " +
                    "the renderer feature cannot be disabled silently.");
            }

            var velocityShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Velocity);
            if (velocityShader == null || !velocityShader.isSupported)
            {
                throw new InvalidOperationException(
                    $"PostProcessRendererFeature requires the supported {ProjectRuntimeContracts.ShaderNames.Velocity} shader.");
            }

            _pass = new PostProcessRenderPass(computeShader, velocityShader);
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
            _mainCamera = Camera.main;
            PostProcessRenderPass.SetMainCamera(_mainCamera);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null)
            {
                return;
            }

            _mainCamera ??= Camera.main;
            if (_mainCamera == null)
            {
                return;
            }

            PostProcessRenderPass.SetMainCamera(_mainCamera);

            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera != _mainCamera)
            {
                return;
            }

            if (!_settings.RunInSceneView && cameraData.isSceneViewCamera)
            {
                return;
            }

            if (!_settings.RunInPreviewCameras && cameraData.isPreviewCamera)
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }
    }
}
