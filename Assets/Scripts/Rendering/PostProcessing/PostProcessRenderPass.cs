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
        private readonly bool _displayPass;
        private readonly ComputeShader _postProcessCS;
        private Vector3 _outputSignature;
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

        public PostProcessRenderPass(ComputeShader postProcessCS, bool displayPass = false)
        {
            _displayPass = displayPass;
            renderPassEvent = displayPass ? RenderPassEvent.AfterRenderingPostProcessing : RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent2D = displayPass ? RenderPassEvent2D.AfterRenderingPostProcessing : RenderPassEvent2D.BeforeRenderingPostProcessing;
            _postProcessCS = UnityEngine.Object.Instantiate(postProcessCS);
            _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
            _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
            _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
            _kernelComposite = _postProcessCS.FindKernel(displayPass ? "DisplayFinal" : "CompositeFinal");
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
            bool bypass = PostProcessRuntimeState.BypassPostProcessEffects ||
                PostProcessRuntimeState.TemporaryBypass;
            AdvancedPostProcessSnapshot advanced = bypass ? default : PostProcessRuntimeState.Advanced;
            float displayGamma =
                bypass ? DisplaySettings.DefaultGamma : PostProcessRuntimeState.DisplayGamma;

            bool bloomActive = !bypass && !_displayPass &&
                ((bloom.active && bloom.IsActive()) || advanced.RequiresBloomTexture);
            bool vignetteActive = !bypass && vignette.active && vignette.IsActive();
            bool caActive = !bypass && ca.active && ca.IsActive();
            bool cgActive = !bypass && cg.active && cg.IsActive();
            bool eigengrauActive = !bypass && eigengrau.active && eigengrau.IsActive();
            bool mbActive = !bypass && mb.active && mb.IsActive();

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
                PostProcessRuntimeState.CompareMode == CompareMode.Off &&
                (_displayPass
                    ? advanced.TemporalPersistenceIntensity > 0f || mbActive
                    : advanced.LightStability > 0f);
            Tonemapping output = stack.GetComponent<Tonemapping>();
            bool hdrOutput = cameraData.isHDROutputActive;

            // HDR display getters throw when HDR support is disabled in Player Settings.
            ColorGamut hdrGamut = hdrOutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB;
            // Unity can report an HDR output before its calibration values are
            // populated. Zero here would turn the DisplayFinal normalization
            // into NaN/Inf and poison the whole frame.
            float paperWhite = hdrOutput
                ? Mathf.Max(output.paperWhite.value, DisplaySettings.DefaultPaperWhite)
                : 1f;
            float peakNits = hdrOutput
                ? Mathf.Max(output.maxNits.value, paperWhite)
                : 0f;
            var signature = new Vector3(paperWhite, peakNits, (float)hdrGamut);
            if (_outputSignature != signature)
            {
                _outputSignature = signature;
                _historyValid = false;
            }

            if (temporalActive && !_temporalWasActive)
            {
                _historyValid = false;
            }

            // Выключенное временное сглаживание отдаёт свою историю обратно.
            // Кадр истории — полноэкранная цель; она переживала выключение и
            // просто занимала память до конца сессии, потому что освобождение
            // висело только на Dispose.
            if (!temporalActive && _temporalWasActive)
            {
                _historyTexture?.Release();
                _historyTexture = null;
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
                ColorGradeSnapshot grade = bypass
                    ? ColorGradeSnapshot.FromLook()
                    : PostProcessRuntimeState.ColorGrade;
                passData.CdlSaturation = grade.CdlSaturation;
                passData.PostDebugView = _displayPass ? (int)PostProcessRuntimeState.DebugView : 0;
                passData.CompareSplit = PostProcessRuntimeState.CompareSplit;
                passData.CompareMode = (int)PostProcessRuntimeState.CompareMode;
                passData.CompareBefore = PostProcessRuntimeState.CompareBefore;
                passData.WhiteBalance = new Vector2(grade.Temperature, grade.Tint);
                passData.CdlSlope = grade.Slope;
                passData.CdlOffset = grade.Offset;
                passData.CdlPower = grade.Power;
                passData.CdlMaster = grade.CdlMaster;
                passData.PrimaryLift = grade.PrimaryLift;
                passData.PrimaryGamma = grade.PrimaryGamma;
                passData.PrimaryGain = grade.PrimaryGain;
                passData.PrimaryOffset = grade.PrimaryOffset;
                passData.PrimaryMaster = grade.PrimaryMaster;
                passData.HueVsSaturation = grade.HueVsSaturation;
                passData.HueVsHue = grade.HueVsHue;
                passData.HueVsLuminance = grade.HueVsLuminance;
                passData.LuminanceVsSaturation = grade.LuminanceVsSaturation;
                passData.SaturationVsSaturation = grade.SaturationVsSaturation;
                passData.Vibrance = grade.Vibrance;
                passData.Hue = grade.Hue;
                passData.ContrastControls = new Vector4(
                    grade.Pivot,
                    grade.Shadows,
                    grade.Highlights,
                    grade.Blacks);
                passData.ContrastControls2 = new Vector3(
                    grade.Whites,
                    grade.Toe,
                    grade.Shoulder);
                passData.BlackPoint = grade.BlackPoint;
                passData.InputWhitePoint = grade.InputWhitePoint;
                passData.HighlightRecovery = grade.HighlightRecovery;
                passData.DisplayGrade0 = new Vector4(
                    grade.WhitePoint,
                    grade.GreyOut,
                    grade.CurveSlope,
                    (int)grade.Transform);
                passData.DisplayGrade1 = new Vector4(
                    grade.ShoulderPower,
                    grade.ToePower,
                    grade.ToeStops,
                    grade.PathToWhiteAmount);
                passData.DisplayGradePathPower = grade.PathToWhitePower;
                passData.MasterCurvePoints = grade.MasterCurve.ToShaderPoints();
                passData.RedCurvePoints = grade.RedCurve.ToShaderPoints();
                passData.GreenCurvePoints = grade.GreenCurve.ToShaderPoints();
                passData.BlueCurvePoints = grade.BlueCurve.ToShaderPoints();
                passData.HueVsHueCurvePoints = grade.HueVsHueCurve.ToShaderPoints();
                passData.HueVsSaturationCurvePoints = grade.HueVsSaturationCurve.ToShaderPoints();
                passData.HueVsLuminanceCurvePoints = grade.HueVsLuminanceCurve.ToShaderPoints();
                passData.LuminanceVsSaturationCurvePoints = grade.LuminanceVsSaturationCurve.ToShaderPoints();
                passData.SaturationVsSaturationCurvePoints = grade.SaturationVsSaturationCurve.ToShaderPoints();
                passData.MasterCurvePointCount = grade.MasterCurve.PointCount;
                passData.RedCurvePointCount = grade.RedCurve.PointCount;
                passData.GreenCurvePointCount = grade.GreenCurve.PointCount;
                passData.BlueCurvePointCount = grade.BlueCurve.PointCount;
                passData.HueVsHueCurvePointCount = grade.HueVsHueCurve.PointCount;
                passData.HueVsSaturationCurvePointCount = grade.HueVsSaturationCurve.PointCount;
                passData.HueVsLuminanceCurvePointCount = grade.HueVsLuminanceCurve.PointCount;
                passData.LuminanceVsSaturationCurvePointCount = grade.LuminanceVsSaturationCurve.PointCount;
                passData.SaturationVsSaturationCurvePointCount = grade.SaturationVsSaturationCurve.PointCount;
                passData.CurveInterpolation = (int)grade.MasterCurve.Interpolation;
                ColorGradeQualifier qualifier = grade.Qualifier;
                passData.Qualifier0 = new Vector4(
                    qualifier.HueCenter,
                    qualifier.HueWidth,
                    qualifier.HueSoftness,
                    qualifier.Enabled ? (qualifier.Invert ? -1f : 1f) : 0f);
                passData.Qualifier1 = new Vector4(
                    qualifier.SaturationCenter,
                    qualifier.SaturationWidth,
                    qualifier.SaturationSoftness,
                    qualifier.LuminanceCenter);
                passData.Qualifier2 = new Vector4(
                    qualifier.LuminanceWidth,
                    qualifier.LuminanceSoftness,
                    qualifier.HueShift,
                    qualifier.Saturation);
                passData.Qualifier3 = new Vector4(
                    qualifier.Exposure,
                    qualifier.Temperature,
                    qualifier.Tint,
                    0f);
                passData.Qualifier4 = new Vector4(
                    qualifier.Lift.x,
                    qualifier.Lift.y,
                    qualifier.Lift.z,
                    0f);
                passData.Qualifier5 = new Vector4(
                    qualifier.Gamma.x,
                    qualifier.Gamma.y,
                    qualifier.Gamma.z,
                    0f);
                passData.Qualifier6 = new Vector4(
                    qualifier.Gain.x,
                    qualifier.Gain.y,
                    qualifier.Gain.z,
                    0f);
                passData.QualifierHueSamples = new Vector4[ColorGradeQualifier.MaxHueSamples];
                passData.QualifierHueSampleCount = Mathf.Min(
                    qualifier.HueSamples.Count,
                    ColorGradeQualifier.MaxHueSamples);
                for (int sampleIndex = 0; sampleIndex < passData.QualifierHueSampleCount; sampleIndex++)
                {
                    passData.QualifierHueSamples[sampleIndex] = new Vector4(
                        qualifier.HueSamples[sampleIndex],
                        qualifier.HueWidth,
                        qualifier.HueSoftness,
                        0f);
                }
                passData.Lut1D = grade.Lut?.Texture1D;
                passData.Lut3D = grade.Lut?.Texture3D;
                passData.LutType = grade.Lut == null ? 0 : (int)grade.Lut.Type;
                passData.LutIntensity = grade.Lut == null ? 0f : grade.LutIntensity;
                passData.LutColorSpace = (int)grade.LutColorSpace;
                passData.LutDomainMin = grade.Lut?.DomainMin ?? Vector3.zero;
                passData.LutDomainMax = grade.Lut?.DomainMax ?? Vector3.one;
                ColorGradeColorManagement management = grade.ColorManagement;
                passData.ColorManagement0 = new Vector4(
                    (int)management.InputColorSpace,
                    (int)management.WorkingColorSpace,
                    (int)management.OutputColorSpace,
                    (int)management.ReferenceMode);
                passData.ColorManagement1 = new Vector4(
                    (int)management.InputTransfer,
                    (int)management.OutputTransfer,
                    (int)management.DynamicRange,
                    0f);
                passData.DisplayPaperWhiteNits = paperWhite;
                passData.DisplayPeakRelative = hdrOutput ? peakNits / paperWhite : 0f;
                passData.HDROutput = _displayPass && hdrOutput;
                passData.HDRGamut = hdrGamut;
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
                    0f,
                    0f);
                passData.HistoryValid = _historyValid;
                passData.Temporal = passData.HistoryValid
                    ? new Vector4(
                        _displayPass ? advanced.TemporalPersistenceIntensity : 0f,
                        advanced.TemporalPersistenceDecay,
                        _displayPass ? 0f : advanced.LightStability,
                        _displayPass && mbActive ? mb.intensity.value : 0f)
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
            UnityEngine.Object.Destroy(_postProcessCS);
            _historyTexture?.Release();
            _historyTexture = null;
            _historyValid = false;
            _temporalWasActive = false;
            _hasViewProjection = false;
        }
    }
}
