#nullable enable

using System;
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
        private bool _missingShaderReported;

        public override void Create()
        {
            _pass?.Dispose();
            _pass = null;

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>("Shaders/PostProcessing/PostProcess");

            if (computeShader == null)
            {
                if (!_missingShaderReported)
                {
                    Debug.LogError(
                        "[PostProcess] Missing Resources/Shaders/PostProcessing/PostProcess.compute. " +
                        "The renderer feature is disabled until a compute shader is assigned.");
                    _missingShaderReported = true;
                }

                return;
            }

            _missingShaderReported = false;
            var velocityShader = Shader.Find("Fodinae/PostProcessing/Velocity") ??
                                 Resources.Load<Shader>("Shaders/PostProcessing/Velocity");
            _pass = new PostProcessRenderPass(computeShader, velocityShader);
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null)
            {
                return;
            }

            ref var cameraData = ref renderingData.cameraData;
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera != Camera.main)
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
