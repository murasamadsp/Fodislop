#nullable enable

using System.Collections.Generic;
using Fodinae.Game;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Fodinae.Rendering.PostProcessing;

public class PostProcessRenderPass : ScriptableRenderPass2D
{
    private const string PASS_NAME = "ComputePostProcessPass";
    private static readonly int InputTexID = Shader.PropertyToID("_InputTex");
    private static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    private static readonly int BaseTexID = Shader.PropertyToID("_BaseTex");
    private static readonly int BloomTexID = Shader.PropertyToID("_BloomTex");
    private static readonly int VelocityTexID = Shader.PropertyToID("_VelocityTex");
    private static readonly int VelocityPropID = Shader.PropertyToID("_Velocity");
    private static readonly int VelocitySpriteTextureID = Shader.PropertyToID("_VelocitySpriteTexture");
    private static readonly int DestTexID = Shader.PropertyToID("_DestTex");
    private static readonly int OutputTexID = Shader.PropertyToID("_OutputTex");
    private static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");
    private static readonly int SourceTexelSizeID = Shader.PropertyToID("_SourceTexelSize");

    private static readonly int BloomThresholdID = Shader.PropertyToID("_BloomThreshold");
    private static readonly int BloomScatterID = Shader.PropertyToID("_BloomScatter");
    private static readonly int BloomTintID = Shader.PropertyToID("_BloomTint");
    private static readonly int BloomIntensityID = Shader.PropertyToID("_BloomIntensity");

    private static readonly int VignetteIntensityID = Shader.PropertyToID("_VignetteIntensity");
    private static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
    private static readonly int VignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness");
    private static readonly int VignetteCenterID = Shader.PropertyToID("_VignetteCenter");

    private static readonly int ChromaticAberrationIntensityID = Shader.PropertyToID("_ChromaticAberrationIntensity");

    private static readonly int ExposureID = Shader.PropertyToID("_Exposure");
    private static readonly int ColorFilterID = Shader.PropertyToID("_ColorFilter");
    private static readonly int ContrastID = Shader.PropertyToID("_Contrast");
    private static readonly int SaturationID = Shader.PropertyToID("_Saturation");

    private static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
    private static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
    private static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
    private static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
    private static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
    private static readonly int TimeID = Shader.PropertyToID("_Time");

    private static readonly int MotionBlurIntensityID = Shader.PropertyToID("_MotionBlurIntensity");
    private static readonly int MotionBlurMaxSamplesID = Shader.PropertyToID("_MotionBlurMaxSamples");

    private readonly ComputeShader _postProcessCS;
    private readonly Material? _velocityMaterial;
    private readonly int _kernelPrefilter;
    private readonly int _kernelDownsample;
    private readonly int _kernelUpsample;
    private readonly int _kernelComposite;

    public PostProcessRenderPass(ComputeShader postProcessCS, Shader? velocityShader)
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        renderPassEvent2D = RenderPassEvent2D.BeforeRenderingPostProcessing;
        _postProcessCS = postProcessCS;
        if (velocityShader != null)
        {
            _velocityMaterial = new Material(velocityShader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
        _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
        _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
        _kernelComposite = _postProcessCS.FindKernel("CompositeFinal");
    }

    private static bool TryGetRemoteRobotRenderer(MotionBlurTag? tag, out SpriteRenderer renderer)
    {
        renderer = null!;
        if (tag == null || !tag.gameObject.activeInHierarchy)
        {
            return false;
        }

        var robot = tag.GetComponent<Robot>();
        if (robot == null || robot.IsLocalPlayer)
        {
            return false;
        }

        renderer = tag.GetComponent<SpriteRenderer>();
        return renderer != null && renderer.enabled && renderer.sprite != null;
    }

    private static Vector2 CalculateUvVelocity(MotionBlurTag tag, Camera camera)
    {
        float screenHeightWorld = camera.orthographic
            ? camera.orthographicSize * 2f
            : 10f;
        float screenWidthWorld = screenHeightWorld * Mathf.Max(camera.aspect, 0.001f);
        Vector2 frameVelocity = tag.Velocity * Mathf.Min(Time.deltaTime, 1f / 20f);
        var uvVelocity = new Vector2(
            frameVelocity.x / Mathf.Max(screenWidthWorld, 0.001f),
            frameVelocity.y / Mathf.Max(screenHeightWorld, 0.001f));

        if (!float.IsFinite(uvVelocity.x) || !float.IsFinite(uvVelocity.y))
        {
            return Vector2.zero;
        }

        var pixelVelocity = new Vector2(
            uvVelocity.x * Mathf.Max(camera.pixelWidth, 1),
            uvVelocity.y * Mathf.Max(camera.pixelHeight, 1));
        pixelVelocity = Vector2.ClampMagnitude(pixelVelocity, 16f);
        return new Vector2(
            pixelVelocity.x / Mathf.Max(camera.pixelWidth, 1),
            pixelVelocity.y / Mathf.Max(camera.pixelHeight, 1));
    }

#if !UNITY_6000_0_OR_NEWER
    private RTHandle? _intermediateColorRT;
    private RTHandle? _bloomPrefilterRT;
    private RTHandle? _velocityRT;
    private readonly RTHandle[] _bloomDownPyramid = new RTHandle[5];
    private readonly RTHandle[] _bloomUpPyramid = new RTHandle[5];

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.renderType != CameraRenderType.Base ||
            renderingData.cameraData.camera.cameraType != CameraType.Game ||
            renderingData.cameraData.camera != Camera.main)
        {
            return;
        }

        var stack = VolumeManager.instance.stack;
        var bloom = stack.GetComponent<BloomComponent>();
        var vignette = stack.GetComponent<VignetteComponent>();
        var ca = stack.GetComponent<ChromaticAberrationComponent>();
        var cg = stack.GetComponent<ColorGradingComponent>();
        var eigengrau = stack.GetComponent<EigengrauComponent>();
        var mb = stack.GetComponent<MotionBlurComponent>();

        bool bloomActive = bloom != null && bloom.active && bloom.IsActive();
        bool vignetteActive = vignette != null && vignette.active && vignette.IsActive();
        bool caActive = ca != null && ca.active && ca.IsActive();
        bool cgActive = cg != null && cg.active && cg.IsActive();
        bool eigengrauActive = eigengrau != null && eigengrau.active && eigengrau.IsActive();
        bool mbActive = mb != null && mb.active && mb.IsActive();

        if (!bloomActive && !vignetteActive && !caActive && !cgActive && !eigengrauActive && !mbActive)
        {
            return;
        }

        var cmd = CommandBufferPool.Get(PASS_NAME);
        cmd.Clear();

        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.bindMS = false;
        desc.enableRandomWrite = true;

        RenderingUtils.ReAllocIfNeeded(ref _intermediateColorRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PPIntermediateColor");
        RenderingUtils.ReAllocIfNeeded(ref _bloomPrefilterRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PPBloomPrefilter");

        int width = desc.width;
        int height = desc.height;

        if (mbActive && _velocityMaterial != null)
        {
            var velocityDesc = desc;
            velocityDesc.enableRandomWrite = false;
            velocityDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocIfNeeded(
                ref _velocityRT,
                velocityDesc,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_PPRobotVelocity");

            cmd.SetRenderTarget(_velocityRT);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewProjectionMatrices(
                renderingData.cameraData.camera.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true));
            IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (!TryGetRemoteRobotRenderer(tag, out var spriteRenderer))
                {
                    continue;
                }

                Vector2 uvVelocity = CalculateUvVelocity(tag, renderingData.cameraData.camera);
                cmd.SetGlobalVector(VelocityPropID, new Vector4(uvVelocity.x, uvVelocity.y, 0f, 0f));
                cmd.SetGlobalTexture(VelocitySpriteTextureID, spriteRenderer.sprite.texture);
                cmd.DrawRenderer(spriteRenderer, _velocityMaterial, 0, 0);
            }
        }

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
            int sourceWidth = width;
            int sourceHeight = height;

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
                cmd.SetComputeVectorParam(
                    _postProcessCS,
                    SourceTexelSizeID,
                    new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
                cmd.SetComputeTextureParam(_postProcessCS, _kernelDownsample, SourceTexID, currentSrc);
                cmd.SetComputeTextureParam(_postProcessCS, _kernelDownsample, DestTexID, _bloomDownPyramid[i]);
                cmd.DispatchCompute(_postProcessCS, _kernelDownsample, Mathf.CeilToInt(dw / 8f), Mathf.CeilToInt(dh / 8f), 1);

                currentSrc = _bloomDownPyramid[i];
                sourceWidth = dw;
                sourceHeight = dh;
            }

            RTHandle currentUp = _bloomDownPyramid[4];
            for (int i = 3; i >= 0; i--)
            {
                int uw = _bloomDownPyramid[i].rt.width;
                int uh = _bloomDownPyramid[i].rt.height;

                cmd.SetComputeVectorParam(_postProcessCS, ScreenSizeID, new Vector4(uw, uh, 1f / uw, 1f / uh));
                cmd.SetComputeVectorParam(
                    _postProcessCS,
                    SourceTexelSizeID,
                    new Vector4(1f / currentUp.rt.width, 1f / currentUp.rt.height, currentUp.rt.width, currentUp.rt.height));
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

        cmd.SetComputeFloatParam(_postProcessCS, ExposureID, cgActive ? cg.exposure.value : 0f);
        cmd.SetComputeVectorParam(_postProcessCS, ColorFilterID, cgActive ? cg.colorFilter.value : Color.white);
        cmd.SetComputeFloatParam(_postProcessCS, ContrastID, cgActive ? cg.contrast.value : 0f);
        cmd.SetComputeFloatParam(_postProcessCS, SaturationID, cgActive ? cg.saturation.value : 1f);

        cmd.SetComputeFloatParam(_postProcessCS, EigengrauIntensityID, eigengrauActive && eigengrau != null ? eigengrau.intensity.value : 0f);
        if (eigengrauActive && eigengrau != null)
        {
            cmd.SetComputeVectorParam(_postProcessCS, EigengrauColorID, eigengrau.color.value);
            cmd.SetComputeFloatParam(_postProcessCS, EigengrauDarknessThresholdID, eigengrau.darknessThreshold.value);
            cmd.SetComputeFloatParam(_postProcessCS, EigengrauNoiseScaleID, eigengrau.noiseScale.value);
            cmd.SetComputeFloatParam(_postProcessCS, EigengrauAnimationSpeedID, eigengrau.animationSpeed.value);
            cmd.SetComputeFloatParam(_postProcessCS, TimeID, Time.time);
        }

        cmd.SetComputeFloatParam(_postProcessCS, MotionBlurIntensityID, mbActive ? mb.intensity.value : 0f);
        cmd.SetComputeIntParam(_postProcessCS, MotionBlurMaxSamplesID, mbActive ? mb.maxSamples.value : 8);

        cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, InputTexID, renderingData.cameraData.renderer.cameraColorTargetHandle);
        cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, OutputTexID, _intermediateColorRT);
        if (mbActive && _velocityRT != null && _velocityMaterial != null)
        {
            cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, VelocityTexID, _velocityRT);
        }
        else
        {
            cmd.SetComputeTextureParam(_postProcessCS, _kernelComposite, VelocityTexID, Texture2D.blackTexture);
        }

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
        public TextureHandle bloomPrefilterTexture;
        public TextureHandle[] bloomDownTextures = new TextureHandle[5];
        public TextureHandle[] bloomUpTextures = new TextureHandle[4];
        public TextureHandle velocityTexture;
        public RenderTextureDescriptor descriptor;
        public Camera camera = null!;
        public Material? velocityMaterial;

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
        public float exposure;
        public Vector4 colorFilter;
        public float contrast;
        public float saturation;

        public bool eigengrauActive;
        public float eigengrauIntensity;
        public Vector4 eigengrauColor;
        public float eigengrauDarknessThreshold;
        public float eigengrauNoiseScale;
        public float eigengrauAnimationSpeed;

        public bool mbActive;
        public bool renderRobotVelocity;
        public float mbIntensity;
        public int mbMaxSamples;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.renderType != CameraRenderType.Base ||
            cameraData.camera.cameraType != CameraType.Game ||
            cameraData.camera != Camera.main)
        {
            return;
        }

        var stack = VolumeManager.instance.stack;
        var bloom = stack.GetComponent<BloomComponent>();
        var vignette = stack.GetComponent<VignetteComponent>();
        var ca = stack.GetComponent<ChromaticAberrationComponent>();
        var cg = stack.GetComponent<ColorGradingComponent>();
        var eigengrau = stack.GetComponent<EigengrauComponent>();
        var mb = stack.GetComponent<MotionBlurComponent>();

        bool bloomActive = bloom != null && bloom.active && bloom.IsActive();
        bool vignetteActive = vignette != null && vignette.active && vignette.IsActive();
        bool caActive = ca != null && ca.active && ca.IsActive();
        bool cgActive = cg != null && cg.active && cg.IsActive();
        bool eigengrauActive = eigengrau != null && eigengrau.active && eigengrau.IsActive();
        bool mbActive = mb != null && mb.active && mb.IsActive();

        if (!bloomActive && !vignetteActive && !caActive && !cgActive && !eigengrauActive && !mbActive)
        {
            return;
        }

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        var activeColor = resourceData.activeColorTexture;
        if (!activeColor.IsValid()) return;

        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.bindMS = false;
        desc.enableRandomWrite = true;

        TextureHandle intermediateTexture = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph,
            desc,
            "_PPIntermediateColor",
            true,
            FilterMode.Bilinear);

        TextureHandle bloomPrefilterTexture = default;
        var bloomDownTextures = new TextureHandle[5];
        var bloomUpTextures = new TextureHandle[4];
        if (bloomActive)
        {
            bloomPrefilterTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                desc,
                "_PPBloomPrefilter",
                true,
                FilterMode.Bilinear);

            var bloomDesc = desc;
            for (int i = 0; i < bloomDownTextures.Length; i++)
            {
                bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                bloomDownTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    bloomDesc,
                    $"_PPBloomDown_{i}",
                    true,
                    FilterMode.Bilinear);

                if (i < bloomUpTextures.Length)
                {
                    bloomUpTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        bloomDesc,
                        $"_PPBloomUp_{i}",
                        true,
                        FilterMode.Bilinear);
                }
            }
        }

        bool renderRobotVelocity = mbActive && _velocityMaterial != null;
        TextureHandle velocityTexture = default;
        if (renderRobotVelocity)
        {
            var velocityDesc = desc;
            velocityDesc.enableRandomWrite = false;
            velocityDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            velocityTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                velocityDesc,
                "_PPRobotVelocity",
                true,
                FilterMode.Point);
        }

        using (var builder = renderGraph.AddUnsafePass<PassData>(PASS_NAME, out var passData, profilingSampler))
        {
            passData.postProcessCS = _postProcessCS;
            passData.kernelPrefilter = _kernelPrefilter;
            passData.kernelDownsample = _kernelDownsample;
            passData.kernelUpsample = _kernelUpsample;
            passData.kernelComposite = _kernelComposite;

            passData.colorTexture = activeColor;
            passData.intermediateTexture = intermediateTexture;
            passData.bloomPrefilterTexture = bloomPrefilterTexture;
            for (int i = 0; i < bloomDownTextures.Length; i++)
            {
                passData.bloomDownTextures[i] = bloomDownTextures[i];
            }
            for (int i = 0; i < bloomUpTextures.Length; i++)
            {
                passData.bloomUpTextures[i] = bloomUpTextures[i];
            }
            passData.descriptor = desc;
            passData.camera = cameraData.camera;
            passData.velocityMaterial = _velocityMaterial;
            passData.velocityTexture = velocityTexture;

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
                passData.exposure = cg.exposure.value;
                passData.colorFilter = cg.colorFilter.value;
                passData.contrast = cg.contrast.value;
                passData.saturation = cg.saturation.value;
            }

            passData.eigengrauActive = eigengrauActive;
            if (eigengrau != null && eigengrauActive)
            {
                passData.eigengrauIntensity = eigengrau.intensity.value;
                passData.eigengrauColor = eigengrau.color.value;
                passData.eigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
                passData.eigengrauNoiseScale = eigengrau.noiseScale.value;
                passData.eigengrauAnimationSpeed = eigengrau.animationSpeed.value;
            }

            passData.mbActive = mbActive;
            passData.renderRobotVelocity = renderRobotVelocity;
            if (mb != null && mbActive)
            {
                passData.mbIntensity = mb.intensity.value;
                passData.mbMaxSamples = mb.maxSamples.value;
            }

            builder.UseTexture(passData.colorTexture, AccessFlags.ReadWrite);
            builder.UseTexture(passData.intermediateTexture, AccessFlags.Write);
            if (passData.bloomActive)
            {
                builder.UseTexture(passData.bloomPrefilterTexture, AccessFlags.ReadWrite);
                for (int i = 0; i < passData.bloomDownTextures.Length; i++)
                {
                    builder.UseTexture(passData.bloomDownTextures[i], AccessFlags.ReadWrite);
                }
                for (int i = 0; i < passData.bloomUpTextures.Length; i++)
                {
                    builder.UseTexture(passData.bloomUpTextures[i], AccessFlags.ReadWrite);
                }
            }
            if (passData.renderRobotVelocity)
            {
                builder.UseTexture(passData.velocityTexture, AccessFlags.ReadWrite);
            }
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                int width = data.descriptor.width;
                int height = data.descriptor.height;

                if (data.renderRobotVelocity && data.velocityMaterial != null)
                {
                    cmd.SetRenderTarget(data.velocityTexture);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.SetViewProjectionMatrices(
                        data.camera.worldToCameraMatrix,
                        GL.GetGPUProjectionMatrix(data.camera.projectionMatrix, true));
                    IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
                    for (int i = 0; i < tags.Count; i++)
                    {
                        var tag = tags[i];
                        if (!TryGetRemoteRobotRenderer(tag, out var spriteRenderer))
                        {
                            continue;
                        }

                        Vector2 uvVelocity = CalculateUvVelocity(tag, data.camera);
                        cmd.SetGlobalVector(VelocityPropID, new Vector4(uvVelocity.x, uvVelocity.y, 0f, 0f));
                        cmd.SetGlobalTexture(VelocitySpriteTextureID, spriteRenderer.sprite.texture);
                        cmd.DrawRenderer(spriteRenderer, data.velocityMaterial, 0, 0);
                    }
                }

                cmd.SetComputeVectorParam(data.postProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                if (data.bloomActive)
                {
                    cmd.SetComputeFloatParam(data.postProcessCS, BloomThresholdID, data.bloomThreshold);
                    cmd.SetComputeFloatParam(data.postProcessCS, BloomScatterID, data.bloomScatter);
                    cmd.SetComputeVectorParam(data.postProcessCS, BloomTintID, data.bloomTint);
                    cmd.SetComputeFloatParam(data.postProcessCS, BloomIntensityID, data.bloomIntensity);

                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelPrefilter, InputTexID, data.colorTexture);
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelPrefilter, DestTexID, data.bloomPrefilterTexture);
                    cmd.DispatchCompute(data.postProcessCS, data.kernelPrefilter, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

                    int downWidth = width;
                    int downHeight = height;
                    int sourceWidth = width;
                    int sourceHeight = height;
                    TextureHandle currentSource = data.bloomPrefilterTexture;
                    for (int i = 0; i < data.bloomDownTextures.Length; i++)
                    {
                        downWidth = Mathf.Max(1, downWidth / 2);
                        downHeight = Mathf.Max(1, downHeight / 2);
                        cmd.SetComputeVectorParam(
                            data.postProcessCS,
                            ScreenSizeID,
                            new Vector4(downWidth, downHeight, 1f / downWidth, 1f / downHeight));
                        cmd.SetComputeVectorParam(
                            data.postProcessCS,
                            SourceTexelSizeID,
                            new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
                        cmd.SetComputeTextureParam(data.postProcessCS, data.kernelDownsample, SourceTexID, currentSource);
                        cmd.SetComputeTextureParam(data.postProcessCS, data.kernelDownsample, DestTexID, data.bloomDownTextures[i]);
                        cmd.DispatchCompute(
                            data.postProcessCS,
                            data.kernelDownsample,
                            Mathf.CeilToInt(downWidth / 8f),
                            Mathf.CeilToInt(downHeight / 8f),
                            1);
                        currentSource = data.bloomDownTextures[i];
                        sourceWidth = downWidth;
                        sourceHeight = downHeight;
                    }

                    TextureHandle currentUp = data.bloomDownTextures[^1];
                    int currentUpWidth = downWidth;
                    int currentUpHeight = downHeight;
                    for (int i = data.bloomUpTextures.Length - 1; i >= 0; i--)
                    {
                        int upWidth = Mathf.Max(1, width >> (i + 1));
                        int upHeight = Mathf.Max(1, height >> (i + 1));
                        cmd.SetComputeVectorParam(
                            data.postProcessCS,
                            ScreenSizeID,
                            new Vector4(upWidth, upHeight, 1f / upWidth, 1f / upHeight));
                        cmd.SetComputeVectorParam(
                            data.postProcessCS,
                            SourceTexelSizeID,
                            new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
                        cmd.SetComputeTextureParam(data.postProcessCS, data.kernelUpsample, SourceTexID, currentUp);
                        cmd.SetComputeTextureParam(data.postProcessCS, data.kernelUpsample, BaseTexID, data.bloomDownTextures[i]);
                        cmd.SetComputeTextureParam(data.postProcessCS, data.kernelUpsample, DestTexID, data.bloomUpTextures[i]);
                        cmd.DispatchCompute(
                            data.postProcessCS,
                            data.kernelUpsample,
                            Mathf.CeilToInt(upWidth / 8f),
                            Mathf.CeilToInt(upHeight / 8f),
                            1);
                        currentUp = data.bloomUpTextures[i];
                        currentUpWidth = upWidth;
                        currentUpHeight = upHeight;
                    }

                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, BloomTexID, currentUp);
                }
                else
                {
                    cmd.SetComputeFloatParam(data.postProcessCS, BloomIntensityID, 0f);
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, BloomTexID, Texture2D.blackTexture);
                }

                cmd.SetComputeVectorParam(data.postProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                cmd.SetComputeFloatParam(data.postProcessCS, VignetteIntensityID, data.vignetteActive ? data.vignetteIntensity : 0f);
                if (data.vignetteActive)
                {
                    cmd.SetComputeVectorParam(data.postProcessCS, VignetteColorID, data.vignetteColor);
                    cmd.SetComputeFloatParam(data.postProcessCS, VignetteSmoothnessID, data.vignetteSmoothness);
                    cmd.SetComputeVectorParam(data.postProcessCS, VignetteCenterID, data.vignetteCenter);
                }

                cmd.SetComputeFloatParam(data.postProcessCS, ChromaticAberrationIntensityID, data.caActive ? data.caIntensity : 0f);

                cmd.SetComputeFloatParam(data.postProcessCS, ExposureID, data.cgActive ? data.exposure : 0f);
                cmd.SetComputeVectorParam(data.postProcessCS, ColorFilterID, data.cgActive ? data.colorFilter : Color.white);
                cmd.SetComputeFloatParam(data.postProcessCS, ContrastID, data.cgActive ? data.contrast : 0f);
                cmd.SetComputeFloatParam(data.postProcessCS, SaturationID, data.cgActive ? data.saturation : 1f);

                cmd.SetComputeFloatParam(data.postProcessCS, EigengrauIntensityID, data.eigengrauActive ? data.eigengrauIntensity : 0f);
                if (data.eigengrauActive)
                {
                    cmd.SetComputeVectorParam(data.postProcessCS, EigengrauColorID, data.eigengrauColor);
                    cmd.SetComputeFloatParam(data.postProcessCS, EigengrauDarknessThresholdID, data.eigengrauDarknessThreshold);
                    cmd.SetComputeFloatParam(data.postProcessCS, EigengrauNoiseScaleID, data.eigengrauNoiseScale);
                    cmd.SetComputeFloatParam(data.postProcessCS, EigengrauAnimationSpeedID, data.eigengrauAnimationSpeed);
                    cmd.SetComputeFloatParam(data.postProcessCS, TimeID, Time.time);
                }

                cmd.SetComputeFloatParam(data.postProcessCS, MotionBlurIntensityID, data.mbActive ? data.mbIntensity : 0f);
                cmd.SetComputeIntParam(data.postProcessCS, MotionBlurMaxSamplesID, data.mbActive ? data.mbMaxSamples : 8);

                cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, InputTexID, data.colorTexture);
                cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, OutputTexID, data.intermediateTexture);
                if (data.renderRobotVelocity)
                {
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, VelocityTexID, data.velocityTexture);
                }
                else
                {
                    cmd.SetComputeTextureParam(data.postProcessCS, data.kernelComposite, VelocityTexID, Texture2D.blackTexture);
                }
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
        _velocityRT?.Release();
        for (int i = 0; i < 5; i++)
        {
            _bloomDownPyramid[i]?.Release();
            _bloomUpPyramid[i]?.Release();
        }
#endif
        if (_velocityMaterial != null)
        {
            CoreUtils.Destroy(_velocityMaterial);
        }
    }
}
