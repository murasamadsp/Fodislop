#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Fodinae.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Fodinae.Rendering.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass2D
    {
        private readonly ComputeShader _postProcessCS;
        private readonly int _kernelPrefilter;
        private readonly int _kernelDownsample;
        private readonly int _kernelUpsample;
        private readonly int _kernelComposite;
        private readonly TextureHandle[] _bloomDownTextures = new TextureHandle[1];
        private readonly TextureHandle[] _bloomUpTextures = new TextureHandle[1];
        private VolumeStack? _cachedVolumeStack;
        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;
        private RTHandle? _historyTexture;
        private GraphicsFormat _historyFormat;
        private bool _historyValid;
        private bool _temporalWasActive;
        private uint _observedCameraGeneration;
        private uint _observedPipelineGeneration;
        private Matrix4x4 _lastViewProjection;
        private bool _hasViewProjection;

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

        public PostProcessRenderPass(ComputeShader postProcessCS)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent2D = RenderPassEvent2D.BeforeRenderingPostProcessing;
            _postProcessCS = postProcessCS;
            _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
            _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
            _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
            _kernelComposite = _postProcessCS.FindKernel("CompositeFinal");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_observedPipelineGeneration != PostProcessRuntimeState.PipelineGeneration)
            {
                _observedPipelineGeneration = PostProcessRuntimeState.PipelineGeneration;
                _historyValid = false;
            }

            if (_observedCameraGeneration != PostProcessRuntimeState.CameraGeneration)
            {
                _observedCameraGeneration = PostProcessRuntimeState.CameraGeneration;
                _historyValid = false;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera != PostProcessRuntimeState.MainCamera)
            {
                return;
            }

            Matrix4x4 viewProjection =
                cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix;
            if (!_hasViewProjection || _lastViewProjection != viewProjection)
            {
                // History has no motion-vector reprojection. Reusing it after
                // the camera moves blends unrelated screen pixels and produces
                // full-frame trails, especially around high-contrast UI and
                // terrain edges.
                _lastViewProjection = viewProjection;
                _hasViewProjection = true;
                _historyValid = false;
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

            // Обход не трогает статики: правится только то, что уходит в кадр.
            // Раньше здесь стояло `PostProcessRuntimeState.Advanced = default` и сброс гаммы, то есть
            // включение тумблера стирало снимок продвинутых эффектов и
            // калибровку дисплея навсегда — выключение обратно возвращало не
            // настройки игрока, а значения по умолчанию, и разница списывалась
            // на «постпроцесс что-то сломал».
            AdvancedPostProcessSnapshot advanced =
                PostProcessRuntimeState.BypassPostProcessEffects ? default : PostProcessRuntimeState.Advanced;
            float displayGamma =
                PostProcessRuntimeState.BypassPostProcessEffects ? DisplaySettings.DefaultGamma : PostProcessRuntimeState.DisplayGamma;

            bool bloomActive =
                (bloom.active && bloom.IsActive()) ||
                advanced.RequiresBloomTexture;
            bool vignetteActive = vignette.active && vignette.IsActive();
            bool caActive = ca.active && ca.IsActive();
            bool cgActive = cg.active && cg.IsActive();
            bool eigengrauActive = eigengrau.active && eigengrau.IsActive();
            bool mbActive = mb.active && mb.IsActive();

            if (PostProcessRuntimeState.BypassPostProcessEffects)
            {
                bloomActive = false;
                vignetteActive = false;
                caActive = false;
                cgActive = false;
                eigengrauActive = false;
                mbActive = false;
            }

            // Досрочного выхода по «ни одного включённого эффекта» здесь нет и
            // быть не может. Тонмап работает в обоих режимах вывода и не
            // выключается ничем: он сжимает HDR каскадного света под диапазон
            // дисплея, и кадр без него не дешевле, а неверен — всё ярче белой
            // точки срезается в плоский белый. Раньше на этом месте стояла
            // проверка, первым слагаемым которой было константное `true`:
            // условие никогда не выполнялось, но читалось как живое.

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid())
            {
                return;
            }

            TextureDesc activeColorDesc = activeColor.GetDescriptor(renderGraph);
            RenderTextureDescriptor historyDesc = cameraData.cameraTargetDescriptor;
            int width = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.width
                : historyDesc.width;
            int height = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.height
                : historyDesc.height;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            // Все временные текстуры наследуют формат, dimension, slices и
            // dynamic-scale флаги настоящего graph-ресурса. Ручная сборка из
            // cameraTargetDescriptor теряла эти свойства и могла дать проходу
            // размер/формат, отличный от реально активного color target.
            TextureDesc desc = activeColorDesc;
            desc.sizeMode = TextureSizeMode.Explicit;
            desc.width = width;
            desc.height = height;
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.enableRandomWrite = true;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.clearBuffer = false;

            historyDesc.width = width;
            historyDesc.height = height;
            historyDesc.graphicsFormat = activeColorDesc.colorFormat;
            historyDesc.depthBufferBits = 0;
            historyDesc.msaaSamples = 1;
            historyDesc.bindMS = false;
            historyDesc.enableRandomWrite = true;

            bool temporalActive = PostProcessRuntimeState.DebugView == PostProcessDebugView.None &&
                (advanced.TemporalPersistenceIntensity > 0f ||
                 advanced.LightStability > 0f ||
                 mbActive);
            if (temporalActive && !_temporalWasActive)
            {
                _historyValid = false;
            }

            _temporalWasActive = temporalActive;
            TextureHandle historyTexture = default;
            if (temporalActive)
            {
                EnsureHistoryTexture(historyDesc);
                historyTexture = renderGraph.ImportTexture(
                    _historyTexture ?? throw new InvalidOperationException(
                        "Post-process history texture allocation failed."));
            }

            desc.name = "_PPIntermediateColor";
            desc.filterMode = FilterMode.Point;
            TextureHandle intermediateTexture = renderGraph.CreateTexture(desc);

            TextureHandle bloomPrefilterTexture = default;
            if (bloomActive)
            {
                var bloomDesc = desc;
                bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                bloomDesc.name = "_PPBloomPrefilter";
                bloomDesc.filterMode = FilterMode.Bilinear;
                bloomPrefilterTexture = renderGraph.CreateTexture(bloomDesc);

                for (int i = 0; i < _bloomDownTextures.Length; i++)
                {
                    bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                    bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                    bloomDesc.name = BloomDownNames[i];
                    _bloomDownTextures[i] = renderGraph.CreateTexture(bloomDesc);
                }

                for (int i = 0; i < _bloomUpTextures.Length; i++)
                {
                    var bloomUpDesc = desc;
                    bloomUpDesc.width = Mathf.Max(1, bloomUpDesc.width >> (i + 1));
                    bloomUpDesc.height = Mathf.Max(1, bloomUpDesc.height >> (i + 1));
                    bloomUpDesc.name = BloomUpNames[i];
                    bloomUpDesc.filterMode = FilterMode.Bilinear;
                    _bloomUpTextures[i] = renderGraph.CreateTexture(bloomUpDesc);
                }
            }

            using (var builder = renderGraph.AddUnsafePass<PostProcessPassData>(PassName, out var passData, profilingSampler))
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
                passData.Width = width;
                passData.Height = height;
                passData.HistoryTexture = historyTexture;

                passData.BloomActive = bloomActive;
                passData.BloomThreshold = bloom.threshold.value;
                passData.BloomSoftKnee = bloom.softKnee.value;
                passData.BloomRadius = bloom.radius.value;
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
                passData.Gamma = displayGamma;
                ColorGradeSnapshot grade = PostProcessRuntimeState.ColorGrade;
                passData.DisplayTransform = cameraData.isHDROutputActive
                    ? (int)DisplayTransform.None
                    : (int)grade.Transform;
                passData.ToneMappingWhitePoint = grade.WhitePoint;
                passData.CurveShape = new Vector4(
                    grade.GreyOut,
                    grade.CurveSlope,
                    grade.ShoulderPower,
                    grade.ToePower);
                passData.CurveRange = new Vector4(
                    grade.ToeStops,
                    grade.PathToWhiteAmount,
                    grade.PathToWhitePower,
                    0f);
                passData.PostDebugView = (int)PostProcessRuntimeState.DebugView;
                passData.CompareSplit = PostProcessRuntimeState.CompareSplit;
                passData.WhiteBalance = new Vector2(grade.Temperature, grade.Tint);
                // HDR FinalBlit сам делает Rec.709 -> hdrDisplayColorGamut,
                // когда cameraData.postProcessEnabled == false (наш контракт).
                // Повторная матрица здесь дважды сжимала цветность. В SDR у
                // FinalBlit такой ветки нет, поэтому wide-color перевод остаётся
                // ответственностью этого прохода.
                passData.OutputGamut = cameraData.isHDROutputActive
                    ? (int)DisplayGamutKind.Rec709
                    : (int)DisplayGamut.Current;
                passData.CdlSlope = grade.Slope;
                passData.CdlOffset = grade.Offset;
                passData.CdlPower = grade.Power;

                if (cameraData.isHDROutputActive)
                {
                    HDROutputSettings output = HDROutputSettings.main;
                    float nativePaperWhite = output.available && output.paperWhiteNits > 10f
                        ? output.paperWhiteNits
                        : DisplaySettings.DefaultPaperWhite;
                    passData.HdrPaperWhiteScale = PostProcessRuntimeState.DisplayPaperWhiteNits / nativePaperWhite;
                    passData.HdrPeakBrightnessScale =
                        PostProcessRuntimeState.DisplayPeakBrightnessNits / nativePaperWhite;
                }
                else
                {
                    passData.HdrPaperWhiteScale = 1f;
                    passData.HdrPeakBrightnessScale = 0f;
                }

                passData.EigengrauActive = eigengrauActive;
                passData.EigengrauIntensity = eigengrau.intensity.value;
                passData.EigengrauColor = eigengrau.color.value;
                passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
                passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
                passData.EigengrauAnimationSpeed = eigengrau.animationSpeed.value;

                passData.Advanced0 = new Vector4(
                    advanced.LocalContrastIntensity,
                    advanced.LensDirtIntensity,
                    advanced.LensDirtScale,
                    advanced.AnamorphicIntensity);
                passData.Advanced1 = new Vector4(
                    advanced.AnamorphicLength,
                    advanced.ChromaticDiffractionIntensity,
                    advanced.HeatRefractionIntensity,
                    advanced.HeatRefractionScale);
                passData.Advanced2 = new Vector4(
                    advanced.GlintIntensity,
                    advanced.GlintThreshold,
                    advanced.VolumetricDustIntensity,
                    advanced.VolumetricDustScale);
                passData.Advanced3 = new Vector4(
                    advanced.VolumetricDustSpeed,
                    advanced.PhosphorMaskIntensity,
                    advanced.DitheringIntensity,
                    0f);
                passData.HistoryValid = _historyValid;
                passData.Temporal = passData.HistoryValid
                    ? new Vector4(
                        advanced.TemporalPersistenceIntensity,
                        advanced.TemporalPersistenceDecay,
                        advanced.LightStability,
                        mbActive ? mb.intensity.value : 0f)
                    : Vector4.zero;
                passData.TemporalActive = temporalActive;
                passData.TimeSeconds = Time.time;

                builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.IntermediateTexture, AccessFlags.ReadWrite);
                if (passData.TemporalActive)
                {
                    builder.UseTexture(passData.HistoryTexture, AccessFlags.ReadWrite);
                }

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

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PostProcessPassData data, UnsafeGraphContext context) => PostProcessPassExecutor.Render(data, context));
            }

            if (temporalActive)
            {
                _historyValid = true;
            }
        }

        private void EnsureHistoryTexture(RenderTextureDescriptor descriptor)
        {
            if (_historyTexture != null &&
                _historyTexture.rt.width == descriptor.width &&
                _historyTexture.rt.height == descriptor.height &&
                _historyFormat == descriptor.graphicsFormat)
            {
                return;
            }

            _historyTexture?.Release();
            _historyTexture = RTHandles.Alloc(
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_PPTemporalHistory");
            _historyFormat = descriptor.graphicsFormat;
            _historyValid = false;
        }

        public void Dispose()
        {
            _historyTexture?.Release();
            _historyTexture = null;
            _historyValid = false;
            _temporalWasActive = false;
            _hasViewProjection = false;
        }
    }
}
