#nullable enable

#if false

#define EFFEKSEER_URP_SUPPORT

using Effekseer.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Fodinae.Rendering.PostProcessing;

public class FodinaeEffekseerRenderPassFeature : ScriptableRendererFeature
{
    public LayerMask LayerMask = ~0;

    class FodinaeEffekseerRenderPassURP : ScriptableRenderPass
    {
        private readonly RenderTargetProperty prop = new();
        private readonly IEffekseerBlitter blitter = new UrpBlitter();
        private LayerMask layerMask;
        private const string RenderPassName = nameof(FodinaeEffekseerRenderPassURP);

        public FodinaeEffekseerRenderPassURP(LayerMask layerMask)
        {
            this.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            this.layerMask = layerMask;
        }

        public void SetLayerMask(LayerMask layerMask)
        {
            this.layerMask = layerMask;
        }

        private static bool IsValidCameraDepthTarget(RenderTargetIdentifier cameraDepthTarget)
        {
            var identifierString = cameraDepthTarget.ToString();
            return !identifierString.Contains("NameID -1") || !identifierString.Contains("InstanceID 0");
        }

        private static void PrepareRenderTargetProperty(RenderTargetProperty renderTargetProperty, RenderTextureDescriptor colorTargetDescriptor, bool requiresDepthTexture, bool xrRendering)
        {
            renderTargetProperty.colorBufferID = null;
            renderTargetProperty.depthTargetIdentifier = null;
            renderTargetProperty.colorTargetRenderTexture = null;
            renderTargetProperty.depthTargetRenderTexture = null;
            renderTargetProperty.ActualScreenSize = null;
            renderTargetProperty.Viewport = null;
            renderTargetProperty.SourceViewport = null;
            renderTargetProperty.isRequiredToChangeViewport = false;
            renderTargetProperty.colorTargetDescriptor = colorTargetDescriptor;
            renderTargetProperty.colorTargetDescriptor.sRGB = false;
            renderTargetProperty.isRequiredToCopyBackground = true;
            renderTargetProperty.renderFeature = RenderFeature.URP;
            renderTargetProperty.canGrabDepth = requiresDepthTexture;
            renderTargetProperty.xrRendering = xrRendering;
        }

#if !UNITY_6000_0_OR_NEWER
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (global::Effekseer.EffekseerSystem.Instance == null) return;
            var xrRendering = renderingData.cameraData.xrRendering;
            PrepareRenderTargetProperty(prop, renderingData.cameraData.cameraTargetDescriptor, renderingData.cameraData.requiresDepthTexture, xrRendering);
            var renderer = renderingData.cameraData.renderer;
            prop.colorTargetIdentifier = renderer.cameraColorTargetHandle;

            var cameraDepthTarget = renderer.cameraDepthTargetHandle;
            var isValidDepth = IsValidCameraDepthTarget(cameraDepthTarget);
            prop.depthTargetIdentifier = isValidDepth ? cameraDepthTarget : null;

            var cmd = CommandBufferPool.Get(RenderPassName);
            global::Effekseer.EffekseerSystem.Instance.renderer.Render(renderingData.cameraData.camera, layerMask.value, prop, cmd, true, blitter);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

#if UNITY_6000_0_OR_NEWER
        private class PassData
        {
            public Camera camera = null!;
            public int layerMask;
            public TextureHandle colorTexture;
            public TextureHandle depthTexture;

            public RenderTargetProperty prop = new();
            public IEffekseerBlitter blitter = new UrpBlitter();
        }

        private static void ExecuteRenderGraphPass(PassData passData, UnsafeGraphContext context)
        {
            var system = global::Effekseer.EffekseerSystem.Instance;
            if (system == null || passData.camera == null) return;

            var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            passData.prop.colorTargetIdentifier = (RenderTargetIdentifier)passData.colorTexture;
            passData.prop.depthTargetIdentifier = passData.depthTexture.IsValid() ? (RenderTargetIdentifier)passData.depthTexture : (RenderTargetIdentifier?)null;
            system.renderer.Render(passData.camera, passData.layerMask, passData.prop, commandBuffer, true, passData.blitter);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (global::Effekseer.EffekseerSystem.Instance == null) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var colorTexture = resourceData.activeColorTexture;
            if (!colorTexture.IsValid()) return;

            var xrRendering = cameraData.xrRendering;

            using (var builder = renderGraph.AddUnsafePass<PassData>("EffekseerPassAfterPP", out var passData, profilingSampler))
            {
                passData.camera = cameraData.camera;
                passData.layerMask = layerMask.value;
                passData.blitter = this.blitter;
                passData.colorTexture = colorTexture;
                builder.UseTexture(passData.colorTexture, AccessFlags.ReadWrite);
                passData.depthTexture = resourceData.activeDepthTexture;
                if (passData.depthTexture.IsValid())
                {
                    builder.UseTexture(passData.depthTexture, AccessFlags.ReadWrite);
                }
                PrepareRenderTargetProperty(passData.prop, cameraData.cameraTargetDescriptor, cameraData.requiresDepthTexture, xrRendering);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData passData, UnsafeGraphContext context) => ExecuteRenderGraphPass(passData, context));
            }
        }
#endif
    }

    class ComputePostProcessPass : ScriptableRenderPass
    {
        private const string PASS_NAME = "ComputePostProcessPass";
        private static readonly int InputTexID = Shader.PropertyToID("_InputTex");
        private static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
        private static readonly int BaseTexID = Shader.PropertyToID("_BaseTex");
        private static readonly int BloomTexID = Shader.PropertyToID("_BloomTex");
        private static readonly int VelocityTexID = Shader.PropertyToID("_VelocityTex");
        private static readonly int DestTexID = Shader.PropertyToID("_DestTex");
        private static readonly int OutputTexID = Shader.PropertyToID("_OutputTex");
        private static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");

        private static readonly int BloomThresholdID = Shader.PropertyToID("_BloomThreshold");
        private static readonly int BloomScatterID = Shader.PropertyToID("_BloomScatter");
        private static readonly int BloomTintID = Shader.PropertyToID("_BloomTint");
        private static readonly int BloomIntensityID = Shader.PropertyToID("_BloomIntensity");

        private static readonly int VignetteIntensityID = Shader.PropertyToID("_VignetteIntensity");
        private static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
        private static readonly int VignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness");
        private static readonly int VignetteCenterID = Shader.PropertyToID("_VignetteCenter");

        private static readonly int ChromaticAberrationIntensityID = Shader.PropertyToID("_ChromaticAberrationIntensity");

        private static readonly int LiftID = Shader.PropertyToID("_Lift");
        private static readonly int GammaID = Shader.PropertyToID("_Gamma");
        private static readonly int GainID = Shader.PropertyToID("_Gain");
        private static readonly int ContrastID = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationID = Shader.PropertyToID("_Saturation");

        private static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
        private static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
        private static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
        private static readonly int TimeID = Shader.PropertyToID("_Time");

        private static readonly int MotionBlurIntensityID = Shader.PropertyToID("_MotionBlurIntensity");
        private static readonly int MotionBlurMaxSamplesID = Shader.PropertyToID("_MotionBlurMaxSamples");

        private readonly ComputeShader _postProcessCS;
        private readonly int _kernelPrefilter;
        private readonly int _kernelDownsample;
        private readonly int _kernelUpsample;
        private readonly int _kernelComposite;

        public ComputePostProcessPass(ComputeShader postProcessCS)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            _postProcessCS = postProcessCS;

            _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
            _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
            _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
            _kernelComposite = _postProcessCS.FindKernel("CompositeFinal");
        }

#if !UNITY_6000_0_OR_NEWER
        private RTHandle? _intermediateColorRT;
        private RTHandle? _bloomPrefilterRT;
        private readonly RTHandle[] _bloomDownPyramid = new RTHandle[5];
        private readonly RTHandle[] _bloomUpPyramid = new RTHandle[5];

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var stack = VolumeManager.instance.stack;
            var bloom = stack.GetComponent<BloomComponent>();
            var vignette = stack.GetComponent<VignetteComponent>();
            var ca = stack.GetComponent<ChromaticAberrationComponent>();
            var cg = stack.GetComponent<ColorGradingComponent>();
            var eigengrau = stack.GetComponent<EigengrauComponent>();
            var mb = stack.GetComponent<MotionBlurComponent>();

            bool bloomActive = bloom != null && bloom.IsActive();
            bool vignetteActive = vignette != null && vignette.IsActive();
            bool caActive = ca != null && ca.IsActive();
            bool cgActive = cg != null && cg.IsActive();
            bool eigengrauActive = eigengrau != null && eigengrau.IsActive();
            bool mbActive = mb != null && mb.IsActive();

            if (!bloomActive && !vignetteActive && !caActive && !cgActive && !eigengrauActive && !mbActive)
            {
                return;
            }

            var cmd = CommandBufferPool.Get(PASS_NAME);
            cmd.Clear();

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;

            RenderingUtils.ReAllocIfNeeded(ref _intermediateColorRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PPIntermediateColor");
            RenderingUtils.ReAllocIfNeeded(ref _bloomPrefilterRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PPBloomPrefilter");

            int width = desc.width;
            int height = desc.height;

            cmd.SetComputeVectorParam(_postProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

            if (bloomActive)
            {
                cmd.SetComputeFloatParam(_postProcessCS, BloomThresholdID, bloom.threshold.value);
                cmd.SetComputeFloatParam(_postProcessCS, BloomScatterID, bloom.scatter.value);
                cmd.SetComputeVectorParam(_postProcessCS, BloomTintID, bloom.tint.value);
                cmd.SetComputeFloatParam(_postProcessCS, BloomIntensityID, bloom.intensity.value);

                cmd.SetComputeTextureParam(_postProcessCS, _kernelPrefilter, InputTexID, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.SetComputeTextureParam(_postProcessCS, _kernelPrefilter, DestTexID, _bloomPrefilterRT);
                cmd.DispatchCompute(_postProcessCS, _kernelPrefilter, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

                int dw = width;
                int dh = height;

                RTHandle currentSrc = _bloomPrefilterRT;
                for (int i = 0; i < 5; i++)
                {
                    dw = Mathf.Max(1, dw / 2);
                    dh = Mathf.Max(1, dh / 2);

                    var levelDesc = desc;
                    levelDesc.width = dw;
                    levelDesc.height = dh;

                    RenderingUtils.ReAllocIfNeeded(ref _bloomDownPyramid[i], levelDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_PPBloomDown_{i}");
                    RenderingUtils.ReAllocIfNeeded(ref _bloomUpPyramid[i], levelDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_PPBloomUp_{i}");

                    cmd.SetComputeVectorParam(_postProcessCS, ScreenSizeID, new Vector4(dw, dh, 1f / dw, 1f / dh));
                    cmd.SetComputeTextureParam(_postProcessCS, _kernelDownsample, SourceTexID, currentSrc);
                    cmd.SetComputeTextureParam(_postProcessCS, _kernelDownsample, DestTexID, _bloomDownPyramid[i]);
                    cmd.DispatchCompute(_postProcessCS, _kernelDownsample, Mathf.CeilToInt(dw / 8f), Mathf.CeilToInt(dh / 8f), 1);

                    currentSrc = _bloomDownPyramid[i];
                }

                RTHandle currentUp = _bloomDownPyramid[4];
                for (int i = 3; i >= 0; i--)
                {
                    int uw = _bloomDownPyramid[i].rt.width;
                    int uh = _bloomDownPyramid[i].rt.height;

                    cmd.SetComputeVectorParam(_postProcessCS, ScreenSizeID, new Vector4(uw, uh, 1f / uw, 1f / uh));
                    cmd.SetComputeTextureParam(_postProcessCS, _kernelUpsample, SourceTexID, currentUp);
                    cmd.SetComputeTextureParam(_postProcessCS, _kernelUpsample, BaseTexID, _bloomDownPyramid[i]);
                    cmd.SetComputeTextureParam(_postProcessCS, _kernelUpsample, DestTexID, _bloomUpPyramid[i]);
                    cmd.DispatchCompute(_postProcessCS, _kernelUpsample, Mathf.CeilToInt(uw / 8f), Mathf.CeilToInt(uh / 8f), 1);

                    currentUp = _bloomUpPyramid[i];
                }

                cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, BloomTexID, currentUp);
            }
            else
            {
                cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, BloomTexID, Texture2D.blackTexture);
            }

            cmd.SetComputeVectorParam(_postProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

            cmd.SetComputeFloatParam(_postProcessCS, VignetteIntensityID, vignetteActive ? vignette.intensity.value : 0f);
            if (vignetteActive)
            {
                cmd.SetComputeVectorParam(_postProcessCS, VignetteColorID, vignette.color.value);
                cmd.SetComputeFloatParam(_postProcessCS, VignetteSmoothnessID, vignette.smoothness.value);
                cmd.SetComputeVectorParam(_postProcessCS, VignetteCenterID, vignette.center.value);
            }

            cmd.SetComputeFloatParam(_postProcessCS, ChromaticAberrationIntensityID, caActive ? ca.intensity.value : 0f);

            cmd.SetComputeVectorParam(_postProcessCS, LiftID, cgActive ? cg.lift.value : new Vector4(1f, 1f, 1f, 0f));
            cmd.SetComputeVectorParam(_postProcessCS, GammaID, cgActive ? cg.gamma.value : Vector4.one);
            cmd.SetComputeVectorParam(_postProcessCS, GainID, cgActive ? cg.gain.value : Vector4.one);
            cmd.SetComputeFloatParam(_postProcessCS, ContrastID, cgActive ? cg.contrast.value : 0f);
            cmd.SetComputeFloatParam(_postProcessCS, SaturationID, cgActive ? cg.saturation.value : 1f);

            cmd.SetComputeFloatParam(_postProcessCS, EigengrauIntensityID, eigengrauActive && eigengrau != null ? eigengrau.intensity.value : 0f);
            if (eigengrauActive && eigengrau != null)
            {
                cmd.SetComputeFloatParam(_postProcessCS, EigengrauNoiseScaleID, eigengrau.noiseScale.value);
                cmd.SetComputeFloatParam(_postProcessCS, EigengrauAnimationSpeedID, eigengrau.animationSpeed.value);
                cmd.SetComputeFloatParam(_postProcessCS, TimeID, Time.time);
            }

            cmd.SetComputeFloatParam(_postProcessCS, MotionBlurIntensityID, mbActive ? mb.intensity.value : 0f);
            cmd.SetComputeIntParam(_postProcessCS, MotionBlurMaxSamplesID, mbActive ? mb.maxSamples.value : 8);

            cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, InputTexID, renderingData.cameraData.renderer.cameraColorTargetHandle);
            cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, OutputTexID, _intermediateColorRT);
            cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, VelocityTexID, Texture2D.blackTexture);

            cmd.DispatchCompute(_postProcessCS, _kernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

            cmd.Blit(_intermediateColorRT, renderingData.cameraData.renderer.cameraColorTargetHandle);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

#if UNITY_6000_0_OR_NEWER
        private class PassData
        {
            public ComputeShader postProcessCS = null!;
            public int kernelPrefilter;
            public int kernelDownsample;
            public int kernelUpsample;
            public int kernelComposite;

            public TextureHandle colorTexture;
            public TextureHandle intermediateTexture;
            public RenderTextureDescriptor descriptor;

            public bool bloomActive;
            public float bloomThreshold;
            public float bloomScatter;
            public Vector4 bloomTint;
            public float bloomIntensity;

            public bool vignetteActive;
            public float vignetteIntensity;
            public Vector4 vignetteColor;
            public float vignetteSmoothness;
            public Vector2 vignetteCenter;

            public bool caActive;
            public float caIntensity;

            public bool cgActive;
            public Vector4 lift;
            public Vector4 gamma;
            public Vector4 gain;
            public float contrast;
            public float saturation;

            public bool eigengrauActive;
            public float eigengrauIntensity;
            public float eigengrauNoiseScale;
            public float eigengrauAnimationSpeed;

            public bool mbActive;
            public float mbIntensity;
            public int mbMaxSamples;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var stack = VolumeManager.instance.stack;
            var bloom = stack.GetComponent<BloomComponent>();
            var vignette = stack.GetComponent<VignetteComponent>();
            var ca = stack.GetComponent<ChromaticAberrationComponent>();
            var cg = stack.GetComponent<ColorGradingComponent>();
            var eigengrau = stack.GetComponent<EigengrauComponent>();
            var mb = stack.GetComponent<MotionBlurComponent>();

            bool bloomActive = bloom != null && bloom.IsActive();
            bool vignetteActive = vignette != null && vignette.IsActive();
            bool caActive = ca != null && ca.IsActive();
            bool cgActive = cg != null && cg.IsActive();
            bool eigengrauActive = eigengrau != null && eigengrau.IsActive();
            bool mbActive = mb != null && mb.IsActive();

            if (!bloomActive && !vignetteActive && !caActive && !cgActive && !eigengrauActive && !mbActive)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid()) return;

            var desc = cameraData.cameraTargetDescriptor;
            desc.enableRandomWrite = true;

            TextureHandle intermediateTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_PPIntermediateColor", true);

            using (var builder = renderGraph.AddUnsafePass<PassData>(PASS_NAME, out var passData, profilingSampler))
            {
                passData.postProcessCS = _postProcessCS;
                passData.kernelPrefilter = _kernelPrefilter;
                passData.kernelDownsample = _kernelDownsample;
                passData.kernelUpsample = _kernelUpsample;
                passData.kernelComposite = _kernelComposite;

                passData.colorTexture = activeColor;
                passData.intermediateTexture = intermediateTexture;
                passData.descriptor = desc;

                passData.bloomActive = bloomActive;
                if (bloom != null && bloomActive)
                {
                    passData.bloomThreshold = bloom.threshold.value;
                    passData.bloomScatter = bloom.scatter.value;
                    passData.bloomTint = bloom.tint.value;
                    passData.bloomIntensity = bloom.intensity.value;
                }

                passData.vignetteActive = vignetteActive;
                if (vignette != null && vignetteActive)
                {
                    passData.vignetteIntensity = vignette.intensity.value;
                    passData.vignetteColor = vignette.color.value;
                    passData.vignetteSmoothness = vignette.smoothness.value;
                    passData.vignetteCenter = vignette.center.value;
                }

                passData.caActive = caActive;
                if (ca != null && caActive)
                {
                    passData.caIntensity = ca.intensity.value;
                }

                passData.cgActive = cgActive;
                if (cg != null && cgActive)
                {
                    passData.lift = cg.lift.value;
                    passData.gamma = cg.gamma.value;
                    passData.gain = cg.gain.value;
                    passData.contrast = cg.contrast.value;
                    passData.saturation = cg.saturation.value;
                }

                passData.eigengrauActive = eigengrauActive;
                if (eigengrau != null && eigengrauActive)
                {
                    passData.eigengrauIntensity = eigengrau.intensity.value;
                    passData.eigengrauNoiseScale = eigengrau.noiseScale.value;
                    passData.eigengrauAnimationSpeed = eigengrau.animationSpeed.value;
                }

                passData.mbActive = mbActive;
                if (mb != null && mbActive)
                {
                    passData.mbIntensity = mb.intensity.value;
                    passData.mbMaxSamples = mb.maxSamples.value;
                }

                builder.UseTexture(passData.colorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.intermediateTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    int width = data.descriptor.width;
                    int height = data.descriptor.height;

                    cmd.SetComputeVectorParam(data.postProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                    cmd.SetComputeFloatParam(data.postProcessCS, VignetteIntensityID, data.vignetteActive ? data.vignetteIntensity : 0f);
                    if (data.vignetteActive)
                    {
                        cmd.SetComputeVectorParam(data.postProcessCS, VignetteColorID, data.vignetteColor);
                        cmd.SetComputeFloatParam(data.postProcessCS, VignetteSmoothnessID, data.vignetteSmoothness);
                        cmd.SetComputeVectorParam(data.postProcessCS, VignetteCenterID, data.vignetteCenter);
                    }

                    cmd.SetComputeFloatParam(data.postProcessCS, ChromaticAberrationIntensityID, data.caActive ? data.caIntensity : 0f);

                    cmd.SetComputeVectorParam(data.postProcessCS, LiftID, data.cgActive ? data.lift : new Vector4(1f, 1f, 1f, 0f));
                    cmd.SetComputeVectorParam(data.postProcessCS, GammaID, data.cgActive ? data.gamma : Vector4.one);
                    cmd.SetComputeVectorParam(data.postProcessCS, GainID, data.cgActive ? data.gain : Vector4.one);
                    cmd.SetComputeFloatParam(data.postProcessCS, ContrastID, data.cgActive ? data.contrast : 0f);
                    cmd.SetComputeFloatParam(data.postProcessCS, SaturationID, data.cgActive ? data.saturation : 1f);

                    cmd.SetComputeFloatParam(data.postProcessCS, EigengrauIntensityID, data.eigengrauActive ? data.eigengrauIntensity : 0f);
                    if (data.eigengrauActive)
                    {
                        cmd.SetComputeFloatParam(data.postProcessCS, EigengrauNoiseScaleID, data.eigengrauNoiseScale);
                        cmd.SetComputeFloatParam(data.postProcessCS, EigengrauAnimationSpeedID, data.eigengrauAnimationSpeed);
                        cmd.SetComputeFloatParam(data.postProcessCS, TimeID, Time.time);
                    }

                    cmd.SetComputeFloatParam(data.postProcessCS, MotionBlurIntensityID, data.mbActive ? data.mbIntensity : 0f);
                    cmd.SetComputeIntParam(data.postProcessCS, MotionBlurMaxSamplesID, data.mbActive ? data.mbMaxSamples : 8);

                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, InputTexID, data.colorTexture);
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, OutputTexID, data.intermediateTexture);
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, VelocityTexID, Texture2D.blackTexture);
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, BloomTexID, Texture2D.blackTexture);

                    cmd.DispatchCompute(data.postProcessCS, data.kernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                    Blitter.BlitCameraTexture(cmd, data.intermediateTexture, data.colorTexture);
                });
            }
        }
#endif

        public void Dispose()
        {
#if !UNITY_6000_0_OR_NEWER
            _intermediateColorRT?.Release();
            _bloomPrefilterRT?.Release();
            for (int i = 0; i < 5; i++)
            {
                _bloomDownPyramid[i]?.Release();
                _bloomUpPyramid[i]?.Release();
            }
#endif
        }
    }

    private FodinaeEffekseerRenderPassURP? m_ScriptablePass;
    private ComputePostProcessPass? m_ComputePass;

    public override void Create()
    {
        m_ScriptablePass = new FodinaeEffekseerRenderPassURP(LayerMask);
        var postProcessCS = Resources.Load<ComputeShader>("Shaders/PostProcessing/PostProcess");
        if (postProcessCS != null)
        {
            m_ComputePass = new ComputePostProcessPass(postProcessCS);
        }
        else
        {
            Debug.LogWarning("[FodinaeEffekseerRenderPassFeature] Could not load Resources/Shaders/PostProcessing/PostProcess compute shader — post-processing disabled");
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        m_ScriptablePass ??= new FodinaeEffekseerRenderPassURP(LayerMask);
        m_ScriptablePass.SetLayerMask(LayerMask);
        renderer.EnqueuePass(m_ScriptablePass);

        if (m_ComputePass != null)
        {
            renderer.EnqueuePass(m_ComputePass);
        }
    }
}

#endif
