#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Fodinae.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Fodinae.Rendering.PostProcessing;

internal static class PostProcessPassExecutor
{
    private static Texture2D? _identityLut1D;
    private static Texture3D? _identityLut3D;

    public static void Render(PostProcessPassData data, UnsafeGraphContext context)
    {
        HDROutputUtils.ConfigureHDROutput(data.PostProcessCS, data.HDRGamut,
            data.HDROutput ? HDROutputUtils.Operation.ColorConversion : HDROutputUtils.Operation.None);
        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        int width = data.Width;
        int height = data.Height;

        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        if (data.BloomActive)
        {
            ExecuteBloom(data, cmd, width, height);
        }
        else
        {
            cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
        }

        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        BindPostProcessParameters(data, cmd);

        cmd.BeginSample("Fodinae.PostProcess.Composite");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
        cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
        cmd.EndSample("Fodinae.PostProcess.Composite");

        cmd.BeginSample("Fodinae.PostProcess.BlitBack");
        Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
        cmd.EndSample("Fodinae.PostProcess.BlitBack");

        if (data.TemporalActive)
        {
            cmd.BeginSample("Fodinae.PostProcess.HistoryCopy");
            cmd.CopyTexture(data.IntermediateTexture, data.HistoryTexture);
            cmd.EndSample("Fodinae.PostProcess.HistoryCopy");
        }
    }

    private static void ExecuteBloom(PostProcessPassData data, CommandBuffer cmd, int width, int height)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomSoftKneeID, data.BloomSoftKnee);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomRadiusID, data.BloomRadius);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, data.BloomScatter);
        cmd.SetComputeVectorParam(data.PostProcessCS, BloomTintID, data.BloomTint);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, data.BloomIntensity);

        int prefilterWidth = Mathf.Max(1, width / 2);
        int prefilterHeight = Mathf.Max(1, height / 2);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            ScreenSizeID,
            new Vector4(
                prefilterWidth,
                prefilterHeight,
                1f / prefilterWidth,
                1f / prefilterHeight));
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            SourceTexelSizeID,
            new Vector4(1f / width, 1f / height, width, height));
        cmd.BeginSample("Fodinae.PostProcess.Bloom.Prefilter");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
        cmd.DispatchCompute(
            data.PostProcessCS,
            data.KernelPrefilter,
            Mathf.CeilToInt(prefilterWidth / 8f),
            Mathf.CeilToInt(prefilterHeight / 8f),
            1);
        cmd.EndSample("Fodinae.PostProcess.Bloom.Prefilter");

        int downWidth = prefilterWidth;
        int downHeight = prefilterHeight;
        int sourceWidth = prefilterWidth;
        int sourceHeight = prefilterHeight;
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
            TextureHandle baseTexture = i == 0
                ? data.BloomPrefilterTexture
                : data.BloomDownTextures[i - 1];
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                ScreenSizeID,
                new Vector4(upWidth, upHeight, 1f / upWidth, 1f / upHeight));
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                SourceTexelSizeID,
                new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, SourceTexID, currentUp);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, BaseTexID, baseTexture);
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

    private static void BindPostProcessParameters(PostProcessPassData data, CommandBuffer cmd)
    {
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
        cmd.SetComputeFloatParam(data.PostProcessCS, CdlSaturationID, data.CdlSaturation);
        cmd.SetComputeFloatParam(data.PostProcessCS, GammaID, data.Gamma);
        // Keep the shader finite even if a stale/partially initialized HDR
        // output profile reaches the pass before display reconciliation.
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPaperWhiteNitsID,
            Mathf.Max(data.DisplayPaperWhiteNits, 1f));
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPeakRelativeID,
            data.DisplayPeakRelative);
        cmd.SetComputeIntParam(data.PostProcessCS, PostDebugViewID, data.PostDebugView);
        cmd.SetComputeFloatParam(data.PostProcessCS, CompareSplitID, data.CompareSplit);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareModeID, data.CompareMode);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareBeforeID, data.CompareBefore ? 1 : 0);
        cmd.SetComputeVectorParam(data.PostProcessCS, WhiteBalanceID, data.WhiteBalance);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlSlopeID, data.CdlSlope);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlOffsetID, data.CdlOffset);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlPowerID, data.CdlPower);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlMasterID, data.CdlMaster);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryLiftID, data.PrimaryLift);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryGammaID, data.PrimaryGamma);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryGainID, data.PrimaryGain);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryOffsetID, data.PrimaryOffset);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryMasterID, data.PrimaryMaster);
        cmd.SetComputeVectorParam(data.PostProcessCS, HueVsSaturationID, data.HueVsSaturation);
        cmd.SetComputeVectorParam(data.PostProcessCS, HueVsHueID, data.HueVsHue);
        cmd.SetComputeVectorParam(data.PostProcessCS, HueVsLuminanceID, data.HueVsLuminance);
        cmd.SetComputeVectorParam(data.PostProcessCS, LuminanceVsSaturationID, data.LuminanceVsSaturation);
        cmd.SetComputeVectorParam(data.PostProcessCS, SaturationVsSaturationID, data.SaturationVsSaturation);
        cmd.SetComputeFloatParam(data.PostProcessCS, VibranceID, data.Vibrance);
        cmd.SetComputeFloatParam(data.PostProcessCS, HueID, data.Hue);
        cmd.SetComputeVectorParam(data.PostProcessCS, ContrastControlsID, data.ContrastControls);
        cmd.SetComputeVectorParam(data.PostProcessCS, ContrastControls2ID, data.ContrastControls2);
        cmd.SetComputeFloatParam(data.PostProcessCS, BlackPointID, data.BlackPoint);
        cmd.SetComputeFloatParam(data.PostProcessCS, InputWhitePointID, data.InputWhitePoint);
        cmd.SetComputeFloatParam(data.PostProcessCS, HighlightRecoveryID, data.HighlightRecovery);
        cmd.SetComputeVectorParam(data.PostProcessCS, DisplayGrade0ID, data.DisplayGrade0);
        cmd.SetComputeVectorParam(data.PostProcessCS, DisplayGrade1ID, data.DisplayGrade1);
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayGradePathPowerID,
            data.DisplayGradePathPower);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, MasterCurveID, data.MasterCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, RedCurveID, data.RedCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, GreenCurveID, data.GreenCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, BlueCurveID, data.BlueCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsHueCurveID,
            data.HueVsHueCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsSaturationCurveID,
            data.HueVsSaturationCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsLuminanceCurveID,
            data.HueVsLuminanceCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            LuminanceVsSaturationCurveID,
            data.LuminanceVsSaturationCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            SaturationVsSaturationCurveID,
            data.SaturationVsSaturationCurvePoints);
        cmd.SetComputeIntParam(data.PostProcessCS, MasterCurvePointCountID, data.MasterCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, RedCurvePointCountID, data.RedCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, GreenCurvePointCountID, data.GreenCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, BlueCurvePointCountID, data.BlueCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsHueCurvePointCountID,
            data.HueVsHueCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsSaturationCurvePointCountID,
            data.HueVsSaturationCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsLuminanceCurvePointCountID,
            data.HueVsLuminanceCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            LuminanceVsSaturationCurvePointCountID,
            data.LuminanceVsSaturationCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            SaturationVsSaturationCurvePointCountID,
            data.SaturationVsSaturationCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, CurveInterpolationID, data.CurveInterpolation);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier0ID, data.Qualifier0);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier1ID, data.Qualifier1);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier2ID, data.Qualifier2);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier3ID, data.Qualifier3);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier4ID, data.Qualifier4);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier5ID, data.Qualifier5);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier6ID, data.Qualifier6);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            QualifierHueSamplesID,
            data.QualifierHueSamples);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            QualifierHueSampleCountID,
            data.QualifierHueSampleCount);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            LutParamsID,
            new Vector4(
                data.LutIntensity,
                data.LutType,
                data.LutColorSpace,
                data.Lut1D != null ? data.Lut1D.width : data.Lut3D != null ? data.Lut3D.width : 0f));
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMinID, data.LutDomainMin);
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMaxID, data.LutDomainMax);
        cmd.SetComputeVectorParam(data.PostProcessCS, ColorManagement0ID, data.ColorManagement0);
        cmd.SetComputeVectorParam(data.PostProcessCS, ColorManagement1ID, data.ColorManagement1);
        // Metal validates every resource declared by a compute kernel, even when
        // the LUT branch is disabled by intensity/type. Bind explicit identity
        // LUTs so neutral grading remains mathematically unchanged.
        cmd.SetComputeTextureParam(
            data.PostProcessCS,
            data.KernelComposite,
            Lut1DID,
            data.Lut1D ?? GetIdentityLut1D());
        cmd.SetComputeTextureParam(
            data.PostProcessCS,
            data.KernelComposite,
            Lut3DID,
            data.Lut3D ?? GetIdentityLut3D());
        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityID, data.EigengrauActive ? data.EigengrauIntensity : 0f);
        if (data.EigengrauActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorID, data.EigengrauColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdID, data.EigengrauDarknessThreshold);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleID, data.EigengrauNoiseScale);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauAnimationSpeedID, data.EigengrauAnimationSpeed);
            cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
        }

        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced0ID, data.Advanced0);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced1ID, data.Advanced1);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced2ID, data.Advanced2);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced3ID, data.Advanced3);
        cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
        cmd.SetComputeVectorParam(data.PostProcessCS, TemporalID, data.Temporal);
        if (data.TemporalActive && data.HistoryValid)
        {
            cmd.SetComputeTextureParam(
                data.PostProcessCS,
                data.KernelComposite,
                HistoryTexID,
                data.HistoryTexture);
        }
        else
        {
            cmd.SetComputeTextureParam(
                data.PostProcessCS,
                data.KernelComposite,
                HistoryTexID,
                Texture2D.blackTexture);
        }
    }

    private static Texture2D GetIdentityLut1D()
    {
        if (_identityLut1D != null)
        {
            return _identityLut1D;
        }

        _identityLut1D = RuntimeTextureFactory.CreateRGBAFloatNoMip(
            2,
            1,
            "PostProcess_IdentityLut1D",
            RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        _identityLut1D.SetPixels([Color.black, Color.white]);
        _identityLut1D.Apply(false, true);
        return _identityLut1D;
    }

    private static Texture3D GetIdentityLut3D()
    {
        if (_identityLut3D != null)
        {
            return _identityLut3D;
        }

        _identityLut3D = RuntimeTextureFactory.CreateRGBAFloat3DNoMip(
            2,
            "PostProcess_IdentityLut3D",
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        _identityLut3D.SetPixels(
        [
            Color.black,
            new Color(1f, 0f, 0f, 1f),
            new Color(0f, 1f, 0f, 1f),
            Color.white,
            new Color(0f, 0f, 1f, 1f),
            new Color(1f, 0f, 1f, 1f),
            new Color(0f, 1f, 1f, 1f),
            Color.white,
        ]);
        _identityLut3D.Apply(false, true);
        return _identityLut3D;
    }
}
