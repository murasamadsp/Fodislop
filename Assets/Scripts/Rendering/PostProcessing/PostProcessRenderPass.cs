#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Game;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
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
        private static readonly int ToneMappingEnabledID = Shader.PropertyToID("_ToneMappingEnabled");
        private static readonly int ToneMappingWhitePointID = Shader.PropertyToID("_ToneMappingWhitePoint");

        private static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
        private static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
        private static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
        private static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
        private static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
        private static readonly int TimeID = Shader.PropertyToID("_Time");

        private static readonly int MotionBlurIntensityID = Shader.PropertyToID("_MotionBlurIntensity");
        private static readonly int MotionBlurMaxSamplesID = Shader.PropertyToID("_MotionBlurMaxSamples");
        private static readonly string[] BloomDownNames =
        {
            "_PPBloomDown_0",
            "_PPBloomDown_1",
            "_PPBloomDown_2",
            "_PPBloomDown_3",
            "_PPBloomDown_4",
        };
        private static readonly string[] BloomUpNames =
        {
            "_PPBloomUp_0",
            "_PPBloomUp_1",
            "_PPBloomUp_2",
            "_PPBloomUp_3",
        };

        private readonly ComputeShader _postProcessCS;
        private readonly Material? _velocityMaterial;
        private readonly int _kernelPrefilter;
        private readonly int _kernelDownsample;
        private readonly int _kernelUpsample;
        private readonly int _kernelComposite;
        private readonly TextureHandle[] _bloomDownTextures = new TextureHandle[5];
        private readonly TextureHandle[] _bloomUpTextures = new TextureHandle[4];
        private VolumeStack? _cachedVolumeStack;
        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;

        private static Camera? _mainCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _mainCamera = null;
        }

        private void RefreshVolumeComponents(VolumeStack stack)
        {
            if (ReferenceEquals(_cachedVolumeStack, stack))
            {
                return;
            }

            _cachedVolumeStack = stack;
            _bloom = stack.GetComponent<BloomComponent>();
            _vignette = stack.GetComponent<VignetteComponent>();
            _chromaticAberration = stack.GetComponent<ChromaticAberrationComponent>();
            _colorGrading = stack.GetComponent<ColorGradingComponent>();
            _eigengrau = stack.GetComponent<EigengrauComponent>();
            _motionBlur = stack.GetComponent<MotionBlurComponent>();
        }

        private static T RequireComponent<T>(T? component, string componentName)
            where T : VolumeComponent
        {
            return component ?? throw new InvalidOperationException(
                $"Post-process VolumeStack is missing required component '{componentName}'.");
        }

        public static void SetMainCamera(Camera? camera)
        {
            _mainCamera = camera;
        }

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

            Robot? robot = tag.CachedRobot;
            if (robot == null || robot.IsLocalPlayer)
            {
                return false;
            }

            SpriteRenderer? cachedRenderer = tag.CachedSpriteRenderer;
            if (cachedRenderer == null)
            {
                return false;
            }

            renderer = cachedRenderer;
            return renderer != null && renderer.enabled && renderer.sprite != null;
        }

        private static bool HasRemoteRobotRenderers()
        {
            IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
            for (int i = 0; i < tags.Count; i++)
            {
                if (TryGetRemoteRobotRenderer(tags[i], out _))
                {
                    return true;
                }
            }

            return false;
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

        private class PassData
        {
            public ComputeShader PostProcessCS = null!;
            public int KernelPrefilter;
            public int KernelDownsample;
            public int KernelUpsample;
            public int KernelComposite;

            public TextureHandle ColorTexture;
            public TextureHandle IntermediateTexture;
            public TextureHandle BloomPrefilterTexture;
            public TextureHandle[] BloomDownTextures = null!;
            public TextureHandle[] BloomUpTextures = null!;
            public TextureHandle VelocityTexture;
            public RenderTextureDescriptor Descriptor;
            public Camera Camera = null!;
            public Material? VelocityMaterial;

            public bool BloomActive;
            public float BloomThreshold;
            public float BloomScatter;
            public Vector4 BloomTint;
            public float BloomIntensity;

            public bool VignetteActive;
            public float VignetteIntensity;
            public Vector4 VignetteColor;
            public float VignetteSmoothness;
            public Vector2 VignetteCenter;

            public bool CaActive;
            public float CaIntensity;

            public bool CgActive;
            public float Exposure;
            public Vector4 ColorFilter;
            public float Contrast;
            public float Saturation;
            public bool ToneMappingEnabled;
            public float ToneMappingWhitePoint;

            public bool EigengrauActive;
            public float EigengrauIntensity;
            public Vector4 EigengrauColor;
            public float EigengrauDarknessThreshold;
            public float EigengrauNoiseScale;
            public float EigengrauAnimationSpeed;

            public bool MbActive;
            public bool RenderRobotVelocity;
            public float MbIntensity;
            public int MbMaxSamples;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera != _mainCamera)
            {
                return;
            }

            var stack = VolumeManager.instance.stack;
            RefreshVolumeComponents(stack);
            BloomComponent bloom = RequireComponent(_bloom, nameof(BloomComponent));
            VignetteComponent vignette = RequireComponent(_vignette, nameof(VignetteComponent));
            ChromaticAberrationComponent ca = RequireComponent(
                _chromaticAberration,
                nameof(ChromaticAberrationComponent));
            ColorGradingComponent cg = RequireComponent(
                _colorGrading,
                nameof(ColorGradingComponent));
            EigengrauComponent eigengrau = RequireComponent(
                _eigengrau,
                nameof(EigengrauComponent));
            MotionBlurComponent mb = RequireComponent(_motionBlur, nameof(MotionBlurComponent));

            bool bloomActive = bloom.active && bloom.IsActive();
            bool vignetteActive = vignette.active && vignette.IsActive();
            bool caActive = ca.active && ca.IsActive();
            bool cgActive = cg.active && cg.IsActive();
            bool eigengrauActive = eigengrau.active && eigengrau.IsActive();
            bool mbActive = mb.active && mb.IsActive();

            // A neutral volume must not allocate or dispatch a full-screen compute
            // pass. Tone mapping is represented by an active ColorGradingComponent,
            // so it remains included in this gate when it is actually required.
            if (!bloomActive &&
                !vignetteActive &&
                !caActive &&
                !cgActive &&
                !eigengrauActive &&
                !mbActive)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid())
            {
                return;
            }

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
                FilterMode.Point);

            TextureHandle bloomPrefilterTexture = default;
            if (bloomActive)
            {
                bloomPrefilterTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    desc,
                    "_PPBloomPrefilter",
                    true,
                    FilterMode.Bilinear);

                var bloomDesc = desc;
                for (int i = 0; i < _bloomDownTextures.Length; i++)
                {
                    bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                    bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                    _bloomDownTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        bloomDesc,
                        BloomDownNames[i],
                        true,
                        FilterMode.Bilinear);

                    if (i < _bloomUpTextures.Length)
                    {
                        _bloomUpTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                            renderGraph,
                            bloomDesc,
                            BloomUpNames[i],
                            true,
                            FilterMode.Bilinear);
                    }
                }
            }

            // Motion blur stays enabled in the volume profile, but do not allocate
            // and clear a full-screen velocity target when there is no remote
            // robot that can contribute to it.
            bool renderRobotVelocity = mbActive &&
                                       _velocityMaterial != null &&
                                       HasRemoteRobotRenderers();
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
                passData.PostProcessCS = _postProcessCS;
                passData.KernelPrefilter = _kernelPrefilter;
                passData.KernelDownsample = _kernelDownsample;
                passData.KernelUpsample = _kernelUpsample;
                passData.KernelComposite = _kernelComposite;

                passData.ColorTexture = activeColor;
                passData.IntermediateTexture = intermediateTexture;
                passData.BloomPrefilterTexture = bloomPrefilterTexture;
                passData.BloomDownTextures = _bloomDownTextures;
                passData.BloomUpTextures = _bloomUpTextures;
                passData.Descriptor = desc;
                passData.Camera = cameraData.camera;
                passData.VelocityMaterial = _velocityMaterial;
                passData.VelocityTexture = velocityTexture;

                passData.BloomActive = bloomActive;
                passData.BloomThreshold = bloom.threshold.value;
                passData.BloomScatter = bloom.scatter.value;
                passData.BloomTint = bloom.tint.value;
                passData.BloomIntensity = bloom.intensity.value;

                passData.VignetteActive = vignetteActive;
                passData.VignetteIntensity = vignette.intensity.value;
                passData.VignetteColor = vignette.color.value;
                passData.VignetteSmoothness = vignette.smoothness.value;
                passData.VignetteCenter = vignette.center.value;

                passData.CaActive = caActive;
                passData.CaIntensity = ca.intensity.value;

                passData.CgActive = cgActive;
                passData.Exposure = cg.exposure.value;
                passData.ColorFilter = cg.colorFilter.value;
                passData.Contrast = cg.contrast.value;
                passData.Saturation = cg.saturation.value;
                passData.ToneMappingEnabled = cg.toneMapping.value;
                passData.ToneMappingWhitePoint = cg.toneMappingWhitePoint.value;

                passData.EigengrauActive = eigengrauActive;
                passData.EigengrauIntensity = eigengrau.intensity.value;
                passData.EigengrauColor = eigengrau.color.value;
                passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
                passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
                passData.EigengrauAnimationSpeed = eigengrau.animationSpeed.value;

                passData.MbActive = mbActive;
                passData.RenderRobotVelocity = renderRobotVelocity;
                passData.MbIntensity = mb.intensity.value;
                passData.MbMaxSamples = mb.maxSamples.value;

                builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.IntermediateTexture, AccessFlags.Write);
                if (passData.BloomActive)
                {
                    builder.UseTexture(passData.BloomPrefilterTexture, AccessFlags.ReadWrite);
                    for (int i = 0; i < passData.BloomDownTextures.Length; i++)
                    {
                        builder.UseTexture(passData.BloomDownTextures[i], AccessFlags.ReadWrite);
                    }

                    for (int i = 0; i < passData.BloomUpTextures.Length; i++)
                    {
                        builder.UseTexture(passData.BloomUpTextures[i], AccessFlags.ReadWrite);
                    }
                }

                if (passData.RenderRobotVelocity)
                {
                    builder.UseTexture(passData.VelocityTexture, AccessFlags.ReadWrite);
                }

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    int width = data.Descriptor.width;
                    int height = data.Descriptor.height;

                    if (data.RenderRobotVelocity && data.VelocityMaterial != null)
                    {
                        cmd.SetRenderTarget(data.VelocityTexture);
                        cmd.ClearRenderTarget(false, true, Color.clear);
                        cmd.SetViewProjectionMatrices(
                            data.Camera.worldToCameraMatrix,
                            GL.GetGPUProjectionMatrix(data.Camera.projectionMatrix, true));
                        IReadOnlyList<MotionBlurTag> tags = MotionBlurTag.ActiveTags;
                        for (int i = 0; i < tags.Count; i++)
                        {
                            var tag = tags[i];
                            if (!TryGetRemoteRobotRenderer(tag, out var spriteRenderer))
                            {
                                continue;
                            }

                            Vector2 uvVelocity = CalculateUvVelocity(tag, data.Camera);
                            cmd.SetGlobalVector(VelocityPropID, new Vector4(uvVelocity.x, uvVelocity.y, 0f, 0f));
                            cmd.SetGlobalTexture(VelocitySpriteTextureID, spriteRenderer.sprite.texture);
                            cmd.DrawRenderer(spriteRenderer, data.VelocityMaterial, 0, 0);
                        }
                    }

                    cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                    if (data.BloomActive)
                    {
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, data.BloomScatter);
                        cmd.SetComputeVectorParam(data.PostProcessCS, BloomTintID, data.BloomTint);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, data.BloomIntensity);

                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Prefilter");
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
                        cmd.DispatchCompute(data.PostProcessCS, data.KernelPrefilter, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                        cmd.EndSample("Fodinae.PostProcess.Bloom.Prefilter");

                        int downWidth = width;
                        int downHeight = height;
                        int sourceWidth = width;
                        int sourceHeight = height;
                        TextureHandle currentSource = data.BloomPrefilterTexture;
                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Downsample");
                        for (int i = 0; i < data.BloomDownTextures.Length; i++)
                        {
                            downWidth = Mathf.Max(1, downWidth / 2);
                            downHeight = Mathf.Max(1, downHeight / 2);
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                ScreenSizeID,
                                new Vector4(downWidth, downHeight, 1f / downWidth, 1f / downHeight));
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                SourceTexelSizeID,
                                new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, SourceTexID, currentSource);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, DestTexID, data.BloomDownTextures[i]);
                            cmd.DispatchCompute(
                                data.PostProcessCS,
                                data.KernelDownsample,
                                Mathf.CeilToInt(downWidth / 8f),
                                Mathf.CeilToInt(downHeight / 8f),
                                1);
                            currentSource = data.BloomDownTextures[i];
                            sourceWidth = downWidth;
                            sourceHeight = downHeight;
                        }
                        cmd.EndSample("Fodinae.PostProcess.Bloom.Downsample");

                        TextureHandle currentUp = data.BloomDownTextures[^1];
                        int currentUpWidth = downWidth;
                        int currentUpHeight = downHeight;
                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Upsample");
                        for (int i = data.BloomUpTextures.Length - 1; i >= 0; i--)
                        {
                            int upWidth = Mathf.Max(1, width >> (i + 1));
                            int upHeight = Mathf.Max(1, height >> (i + 1));
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                ScreenSizeID,
                                new Vector4(upWidth, upHeight, 1f / upWidth, 1f / upHeight));
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                SourceTexelSizeID,
                                new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, SourceTexID, currentUp);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, BaseTexID, data.BloomDownTextures[i]);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, DestTexID, data.BloomUpTextures[i]);
                            cmd.DispatchCompute(
                                data.PostProcessCS,
                                data.KernelUpsample,
                                Mathf.CeilToInt(upWidth / 8f),
                                Mathf.CeilToInt(upHeight / 8f),
                                1);
                            currentUp = data.BloomUpTextures[i];
                            currentUpWidth = upWidth;
                            currentUpHeight = upHeight;
                        }
                        cmd.EndSample("Fodinae.PostProcess.Bloom.Upsample");

                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, currentUp);
                    }
                    else
                    {
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
                    }

                    cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                    cmd.SetComputeFloatParam(data.PostProcessCS, VignetteIntensityID, data.VignetteActive ? data.VignetteIntensity : 0f);
                    if (data.VignetteActive)
                    {
                        cmd.SetComputeVectorParam(data.PostProcessCS, VignetteColorID, data.VignetteColor);
                        cmd.SetComputeFloatParam(data.PostProcessCS, VignetteSmoothnessID, data.VignetteSmoothness);
                        cmd.SetComputeVectorParam(data.PostProcessCS, VignetteCenterID, data.VignetteCenter);
                    }

                    cmd.SetComputeFloatParam(data.PostProcessCS, ChromaticAberrationIntensityID, data.CaActive ? data.CaIntensity : 0f);

                    cmd.SetComputeFloatParam(data.PostProcessCS, ExposureID, data.CgActive ? data.Exposure : 0f);
                    cmd.SetComputeVectorParam(data.PostProcessCS, ColorFilterID, data.CgActive ? data.ColorFilter : Color.white);
                    cmd.SetComputeFloatParam(data.PostProcessCS, ContrastID, data.CgActive ? data.Contrast : 0f);
                    cmd.SetComputeFloatParam(data.PostProcessCS, SaturationID, data.CgActive ? data.Saturation : 1f);
                    cmd.SetComputeIntParam(
                        data.PostProcessCS,
                        ToneMappingEnabledID,
                        data.CgActive && data.ToneMappingEnabled ? 1 : 0);
                    cmd.SetComputeFloatParam(
                        data.PostProcessCS,
                        ToneMappingWhitePointID,
                        data.CgActive ? data.ToneMappingWhitePoint : 1f);

                    cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityID, data.EigengrauActive ? data.EigengrauIntensity : 0f);
                    if (data.EigengrauActive)
                    {
                        cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorID, data.EigengrauColor);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdID, data.EigengrauDarknessThreshold);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleID, data.EigengrauNoiseScale);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauAnimationSpeedID, data.EigengrauAnimationSpeed);
                        cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, Time.time);
                    }

                    cmd.SetComputeFloatParam(data.PostProcessCS, MotionBlurIntensityID, data.MbActive ? data.MbIntensity : 0f);
                    cmd.SetComputeIntParam(data.PostProcessCS, MotionBlurMaxSamplesID, data.MbMaxSamples);

                    cmd.BeginSample("Fodinae.PostProcess.Composite");
                    cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
                    cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
                    if (data.RenderRobotVelocity)
                    {
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, VelocityTexID, data.VelocityTexture);
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, VelocityTexID, Texture2D.blackTexture);
                    }

                    cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                    cmd.EndSample("Fodinae.PostProcess.Composite");

                    cmd.BeginSample("Fodinae.PostProcess.BlitBack");
                    Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
                    cmd.EndSample("Fodinae.PostProcess.BlitBack");
                });
            }
        }

        public void Dispose()
        {
            if (_velocityMaterial != null)
            {
                CoreUtils.Destroy(_velocityMaterial);
            }
        }
    }
}
