#nullable enable

using System;
using Kern.Core;
using Kern.Rendering.PostProcessing.Scopes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
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
            private ComputeShader? _computeShader = null;

            public ComputeShader? ComputeShader => _computeShader;
        }

        [SerializeField]
        private Settings _settings = new();

        private WorldBloomRenderPass? _worldBloomPass;
        private PostProcessRenderPass? _displayPass;
        private ScopesRenderPass? _scopesPass;
        private Camera? _mainCamera;
        private WorldRasterGridPass? _worldRasterPass;
        private readonly WorldRasterDepthPass _worldDepthPass = new();
        private readonly WorldRasterPresentationPass _worldPresentationPass = new();
        private readonly WorldRasterTargetObservation _rasterObservation = new();

        private sealed class WorldRasterTargetObservation : ScriptableRenderPass2D
        {
            public WorldRasterTargetObservation()
            {
                renderPassEvent2D = RenderPassEvent2D.BeforeRendering;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                TextureHandle color = frameData.Get<UniversalResourceData>().activeColorTexture;
                RenderTargetInfo descriptor = renderGraph.GetRenderTargetInfo(color);
                TextureHandle depth = frameData.Get<UniversalResourceData>().activeDepthTexture;
                if (depth.IsValid())
                {
                    RenderTargetInfo depthDescriptor = renderGraph.GetRenderTargetInfo(depth);
                    if (descriptor.width != depthDescriptor.width || descriptor.height != depthDescriptor.height ||
                        descriptor.volumeDepth != depthDescriptor.volumeDepth || descriptor.msaaSamples != depthDescriptor.msaaSamples)
                    {
                        throw new InvalidOperationException("World raster color and depth attachments differ before Renderer2D.");
                    }
                }
                PostProcessRuntimeState.DiagnosticWorldRasterTarget?.Invoke(camera.camera,
                    new Vector2Int(descriptor.width, descriptor.height));
            }
        }

        internal bool RendersCamera(Camera camera) => _mainCamera == camera ||
            (_mainCamera == GameplayCamera.Resolve() && camera == PostProcessRuntimeState.DiagnosticOffscreenCamera);
        internal PostProcessWorkloadSnapshot? SceneWorkload => _worldBloomPass?.LatestWorkload;
        internal PostProcessWorkloadSnapshot? DisplayWorkload => _displayPass?.LatestWorkload;

        public override void Create()
        {
            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
            _scopesPass?.Dispose();
            _scopesPass = null;
            if (PostProcessRuntimeState.MainCamera == _mainCamera)
            {
                PostProcessRuntimeState.SetMainCamera(null);
            }

            _mainCamera = null;
            _worldRasterPass = null;
        }

        private void EnsurePassCreated(Camera gameplayCamera)
        {
            // Renderer features outlive a game scope. Rebind the owner even
            // when the existing passes survive and only a diagnostic camera
            // renders this frame (the batch production image path).
            if (_mainCamera != gameplayCamera || PostProcessRuntimeState.MainCamera != gameplayCamera)
            {
                _mainCamera = gameplayCamera;
                PostProcessRuntimeState.SetMainCamera(gameplayCamera);
            }
            // Живой объект прохода ещё не значит живой шейдер: сборка плеера
            // выгружает несохранённую копию ComputeShader. Без этой проверки
            // постпроцесс в редакторе молча пропадал до перезагрузки домена.
            if (_displayPass != null && _displayPass.IsShaderAlive && _worldBloomPass is { IsAlive: true })
            {
                return;
            }

            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
            // Ниже scopes создаются заново; старый проход освобождается здесь,
            // иначе при пересоздании он утекал бы вместе со своими буферами.
            _scopesPass?.Dispose();
            _scopesPass = null;

            var computeShader = _settings.ComputeShader != null
                ? _settings.ComputeShader
                : Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

            if (computeShader == null)
            {
                throw new InvalidOperationException(
                    "PostProcessRendererFeature requires PostProcess.compute; " +
                    "the renderer feature cannot be disabled silently.");
            }

            _worldBloomPass = new WorldBloomRenderPass();
            _displayPass = new PostProcessRenderPass(computeShader);
            _displayPass.ConfigureInput(ScriptableRenderPassInput.Color);

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
                (cameraData.camera.targetTexture != null &&
                 cameraData.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera))
            {
                return;
            }

            Camera? targetCamera = GameplayCamera.Resolve();
            if (targetCamera != null && cameraData.camera != targetCamera &&
                cameraData.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera)
            {
                return;
            }

            EnsurePassCreated(targetCamera ?? cameraData.camera);
            if (_worldRasterPass == null)
            {
                SortingLayer[] layers = SortingLayer.layers;
                _worldRasterPass = new WorldRasterGridPass(layers[0].id);
            }

            _worldRasterPass.Prepare(ref cameraData);
            renderer.EnqueuePass(_worldDepthPass);
            renderer.EnqueuePass(_worldRasterPass);
            renderer.EnqueuePass(_worldPresentationPass);
            if (PostProcessRuntimeState.DiagnosticWorldRasterTarget != null)
            {
                renderer.EnqueuePass(_rasterObservation);
            }
            if (_displayPass == null || _worldBloomPass == null)
            {
                return;
            }

            if (cameraData.camera == targetCamera &&
                (_mainCamera != cameraData.camera ||
                 PostProcessRuntimeState.MainCamera != cameraData.camera))
            {
                _mainCamera = cameraData.camera;
                PostProcessRuntimeState.SetMainCamera(_mainCamera);
            }

            if (PostProcessRuntimeState.SkipPasses)
            {
                // World-grid presentation is required even during an artistic-effect bypass.
                renderer.EnqueuePass(_displayPass!);
                return;
            }

            bool scopesEnabled = _scopesPass != null && ScopesRenderPass.Enabled;
            renderer.EnqueuePass(_worldBloomPass);
            if (scopesEnabled && ScopesRenderPass.SourceMode == ScopesSourceMode.Before)
            {
                // Capture after Kern's scene-linear bloom pass, but before URP
                // applies post exposure and tonemapping.
                renderer.EnqueuePass(_scopesPass!);
            }

            renderer.EnqueuePass(_displayPass!);
            if (scopesEnabled && ScopesRenderPass.SourceMode == ScopesSourceMode.After)
            {
                renderer.EnqueuePass(_scopesPass!);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _worldBloomPass?.Dispose();
            _worldBloomPass = null;
            _displayPass = null;
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
