#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Fodinae.Rendering.PostProcessing;

internal sealed class PostProcessPassData
{
    public ComputeShader PostProcessCS = null!;
    public bool HDROutput;
    public ColorGamut HDRGamut;
    public int KernelPrefilter;
    public int KernelDownsample;
    public int KernelUpsample;
    public int KernelComposite;

    public TextureHandle ColorTexture;
    public TextureHandle IntermediateTexture;
    public TextureHandle BloomPrefilterTexture;
    public TextureHandle[] BloomDownTextures = null!;
    public TextureHandle[] BloomUpTextures = null!;
    public TextureHandle HistoryTexture;
    public int Width;
    public int Height;

    public bool BloomActive;
    public float BloomThreshold;
    public float BloomSoftKnee;
    public float BloomRadius;
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
    public float CdlSaturation;
    public float Gamma;
    public float DisplayPaperWhiteNits;
    public float DisplayPeakRelative;
    public int PostDebugView;
    public float CompareSplit;
    public int CompareMode;
    public bool CompareBefore;
    public Vector2 WhiteBalance;

    public Vector4 CdlSlope;
    public Vector4 CdlOffset;
    public Vector4 CdlPower;
    public Vector3 CdlMaster;
    public Vector4 PrimaryLift;
    public Vector4 PrimaryGamma;
    public Vector4 PrimaryGain;
    public Vector4 PrimaryOffset;
    public Vector4 PrimaryMaster;
    public Vector4 HueVsSaturation;
    public Vector4 HueVsHue;
    public Vector4 HueVsLuminance;
    public Vector4 LuminanceVsSaturation;
    public Vector4 SaturationVsSaturation;
    public float Vibrance;
    public float Hue;
    public Vector4 ContrastControls;
    public Vector3 ContrastControls2;
    public float BlackPoint;
    public float InputWhitePoint;
    public float HighlightRecovery;
    public Vector4 DisplayGrade0;
    public Vector4 DisplayGrade1;
    public float DisplayGradePathPower;
    public Vector4[] MasterCurvePoints = null!;
    public Vector4[] RedCurvePoints = null!;
    public Vector4[] GreenCurvePoints = null!;
    public Vector4[] BlueCurvePoints = null!;
    public Vector4[] HueVsHueCurvePoints = null!;
    public Vector4[] HueVsSaturationCurvePoints = null!;
    public Vector4[] HueVsLuminanceCurvePoints = null!;
    public Vector4[] LuminanceVsSaturationCurvePoints = null!;
    public Vector4[] SaturationVsSaturationCurvePoints = null!;
    public int MasterCurvePointCount;
    public int RedCurvePointCount;
    public int GreenCurvePointCount;
    public int BlueCurvePointCount;
    public int HueVsHueCurvePointCount;
    public int HueVsSaturationCurvePointCount;
    public int HueVsLuminanceCurvePointCount;
    public int LuminanceVsSaturationCurvePointCount;
    public int SaturationVsSaturationCurvePointCount;
    public int CurveInterpolation;
    public Vector4 Qualifier0;
    public Vector4 Qualifier1;
    public Vector4 Qualifier2;
    public Vector4 Qualifier3;
    public Vector4 Qualifier4;
    public Vector4 Qualifier5;
    public Vector4 Qualifier6;
    public Vector4[] QualifierHueSamples = null!;
    public int QualifierHueSampleCount;
    public Texture2D? Lut1D;
    public Texture3D? Lut3D;
    public int LutType;
    public float LutIntensity;
    public int LutColorSpace;
    public Vector3 LutDomainMin;
    public Vector3 LutDomainMax;
    public Vector4 ColorManagement0;
    public Vector4 ColorManagement1;

    public bool EigengrauActive;
    public float EigengrauIntensity;
    public Vector4 EigengrauColor;
    public float EigengrauDarknessThreshold;
    public float EigengrauNoiseScale;
    public float EigengrauAnimationSpeed;

    public Vector4 Advanced0;
    public Vector4 Advanced1;
    public Vector4 Advanced2;
    public Vector4 Advanced3;
    public Vector4 Temporal;
    public bool HistoryValid;
    public bool TemporalActive;
    public float TimeSeconds;
}
