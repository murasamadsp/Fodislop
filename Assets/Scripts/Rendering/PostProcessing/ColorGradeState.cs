#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Слой цветового конвейера — то, что можно обойти и включить в одиночку.
/// </summary>
public enum ColorGradeLayer
{
    Exposure = 0,
    WhiteBalance = 1,
    Cdl = 2,
    Saturation = 3,
    Contrast = 4,
    Curve = 5,
}

/// <summary>
/// Изменяемое состояние грейда: то, что крутит рабочее место.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНО ОТ <see cref="ColorGradeSnapshot"/>. Снимок неизменяем и
/// уходит в шейдер; состояние изменяемо и живёт в инструменте. Разделение не
/// формальность: обход и соло не должны попадать в шейдер как отдельные флаги,
/// они обязаны превращаться в НЕЙТРАЛЬНЫЕ ЗНАЧЕНИЯ ещё здесь. Иначе в шейдере
/// появилась бы ветка на каждый слой, и кадр в отладке считался бы не тем же
/// кодом, что кадр в игре, — то есть отладка врала бы.
///
/// В конфиг это не попадает намеренно: грейд — авторское решение, а не
/// настройка игрока. Хранение — <see cref="ColorGradeFile"/>.
/// </remarks>
public sealed class ColorGradeState
{
    private const int LayerCount = 6;

    public const float ExposureMin = -4f;
    public const float ExposureMax = 4f;
    public const float ExposureHardMin = -8f;
    public const float ExposureHardMax = 8f;
    public const float TemperatureMin = -100f;
    public const float TemperatureMax = 100f;
    public const float SlopeMin = 0f;
    public const float SlopeMax = 4f;
    public const float OffsetMin = -0.5f;
    public const float OffsetMax = 0.5f;
    public const float PowerMin = 0.1f;
    public const float PowerMax = 4f;
    public const float SaturationMin = 0f;
    public const float SaturationMax = 2f;
    public const float CdlSaturationMin = 0f;
    public const float CdlSaturationMax = 2f;
    public const float VibranceMin = -1f;
    public const float VibranceMax = 1f;
    public const float HueMin = -180f;
    public const float HueMax = 180f;
    public const float HueCenterMin = 0f;
    public const float HueCenterMax = 360f;
    public const float HueWidthMin = 0f;
    public const float HueWidthMax = 180f;
    public const float HueFeatherMin = 0f;
    public const float HueFeatherMax = 180f;
    public const float HueSaturationMin = 0f;
    public const float HueSaturationMax = 2f;
    public const float HueShiftMin = -180f;
    public const float HueShiftMax = 180f;
    public const float HueLuminanceMin = -1f;
    public const float HueLuminanceMax = 1f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float PivotMin = 0.1f;
    public const float PivotMax = 0.9f;
    public const float TonalAdjustmentMin = -0.5f;
    public const float TonalAdjustmentMax = 0.5f;
    public const float WhitePointMin = 0.25f;
    public const float WhitePointMax = 8f;
    public const float BlackPointMin = 0f;
    public const float BlackPointMax = 0.99f;
    public const float InputWhitePointMin = 0.01f;
    public const float InputWhitePointMax = 64f;
    public const float HighlightRecoveryMin = 0f;
    public const float HighlightRecoveryMax = 1f;
    public const float GreyOutMin = 0.05f;
    public const float GreyOutMax = 0.5f;
    public const float CurveSlopeMin = 0.5f;
    public const float CurveSlopeMax = 2f;
    public const float CurvePowerMin = 1f;
    public const float CurvePowerMax = 8f;
    public const float ToeStopsMin = 4f;
    public const float ToeStopsMax = 20f;
    public const float PathToWhiteAmountMin = 0f;
    public const float PathToWhiteAmountMax = 1f;
    public const float PathToWhitePowerMin = 1f;
    public const float PathToWhitePowerMax = 8f;

    private readonly bool[] _bypass = new bool[LayerCount];
    private readonly bool[] _enabled = new bool[LayerCount];
    private readonly Stack<ColorGradeSnapshot> _undo = [];
    private readonly Stack<ColorGradeSnapshot> _redo = [];
    private ColorGradeSnapshot? _historyFrame;

    public ColorGradeState()
    {
        ResetToLook();
    }

    public DisplayTransform Transform { get; set; }

    public float Exposure { get; set; }

    public float Contrast { get; set; }

    public float Pivot { get; set; }
    public float Shadows { get; set; }
    public float Highlights { get; set; }
    public float Blacks { get; set; }
    public float Whites { get; set; }
    public float Toe { get; set; }
    public float Shoulder { get; set; }

    public float Saturation { get; set; }

    public float CdlSaturation { get; set; }

    public float Vibrance { get; set; }

    public float Hue { get; set; }

    public float Temperature { get; set; }

    public float Tint { get; set; }

    public Vector3 Slope { get; set; }

    public Vector3 Offset { get; set; }

    public Vector3 Power { get; set; }

    public Vector3 PrimaryLift { get; set; }

    public Vector3 PrimaryGamma { get; set; }

    public Vector3 PrimaryGain { get; set; }

    public Vector3 PrimaryOffset { get; set; }

    public Vector4 PrimaryMaster { get; set; }

    public Vector3 CdlMaster { get; set; }

    public Vector4 HueVsSaturation { get; set; }

    public Vector4 HueVsHue { get; set; }

    public Vector4 HueVsLuminance { get; set; }

    public Vector4 LuminanceVsSaturation { get; set; }
    public Vector4 SaturationVsSaturation { get; set; }

    public float WhitePoint { get; set; }

    public float BlackPoint { get; set; }

    public float InputWhitePoint { get; set; }

    public float HighlightRecovery { get; set; }

    public float GreyOut { get; set; }

    public float CurveSlope { get; set; }

    public float ShoulderPower { get; set; }

    public float ToePower { get; set; }

    public float ToeStops { get; set; }

    public float PathToWhiteAmount { get; set; }

    public float PathToWhitePower { get; set; }

    public ColorGradeCurve MasterCurve { get; } = new();

    public ColorGradeCurve RedCurve { get; } = new();

    public ColorGradeCurve GreenCurve { get; } = new();

    public ColorGradeCurve BlueCurve { get; } = new();

    public ColorGradeCurve HueVsHueCurve { get; } = new();

    public ColorGradeCurve HueVsSaturationCurve { get; } = new();

    public ColorGradeCurve HueVsLuminanceCurve { get; } = new();

    public ColorGradeCurve LuminanceVsSaturationCurve { get; } = new();

    public ColorGradeCurve SaturationVsSaturationCurve { get; } = new();

    public ColorGradeQualifier Qualifier { get; } = new();

    public ColorGradeCubeLut? Lut { get; private set; }

    public ColorGradeColorManagement ColorManagement { get; } = new();

    public string LutPath { get; private set; } = string.Empty;

    public float LutIntensity { get; set; }

    public ColorGradeLutColorSpace LutColorSpace { get; set; }

    /// <summary>
    /// Единственный невыключенный слой, либо <c>null</c>. Соло — не то же
    /// самое, что обход всех прочих: выйти из соло надо одним движением, иначе
    /// набор обходов после него уже не восстановить.
    /// </summary>
    public ColorGradeLayer? Solo { get; set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void BeginHistoryFrame()
    {
        _historyFrame ??= ToAuthoredSnapshot();
    }

    public void CommitHistoryFrame()
    {
        if (!_historyFrame.HasValue)
        {
            return;
        }

        ColorGradeSnapshot before = _historyFrame.Value;
        _historyFrame = null;
        ColorGradeSnapshot after = ToAuthoredSnapshot();
        if (SnapshotsEqual(before, after))
        {
            return;
        }

        _undo.Push(before);
        _redo.Clear();
        TrimHistory(_undo);
    }

    public void CancelHistoryFrame() => _historyFrame = null;

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        ColorGradeSnapshot current = ToAuthoredSnapshot();
        ColorGradeSnapshot previous = _undo.Pop();
        _redo.Push(current);
        RestoreSnapshot(previous);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        ColorGradeSnapshot current = ToAuthoredSnapshot();
        ColorGradeSnapshot next = _redo.Pop();
        _undo.Push(current);
        RestoreSnapshot(next);
        return true;
    }

    public bool IsBypassed(ColorGradeLayer layer) => _bypass[(int)layer];

    public bool IsEnabled(ColorGradeLayer layer) => _enabled[(int)layer];

    public void SetEnabled(ColorGradeLayer layer, bool enabled)
    {
        _enabled[(int)layer] = enabled;
        if (!enabled && Solo == layer)
        {
            Solo = null;
        }
    }

    public int EnabledMask
    {
        get
        {
            int mask = 0;
            for (int index = 0; index < LayerCount; index++)
            {
                if (_enabled[index])
                {
                    mask |= 1 << index;
                }
            }

            return mask;
        }
        set
        {
            int validMask = value & ((1 << LayerCount) - 1);
            for (int index = 0; index < LayerCount; index++)
            {
                _enabled[index] = (validMask & (1 << index)) != 0;
            }
        }
    }

    public void SetBypassed(ColorGradeLayer layer, bool bypassed) =>
        _bypass[(int)layer] = bypassed;

    public int BypassMask
    {
        get
        {
            int mask = 0;
            for (int index = 0; index < LayerCount; index++)
            {
                if (_bypass[index])
                {
                    mask |= 1 << index;
                }
            }

            return mask;
        }
        set
        {
            int validMask = value & ((1 << LayerCount) - 1);
            for (int index = 0; index < LayerCount; index++)
            {
                _bypass[index] = (validMask & (1 << index)) != 0;
            }
        }
    }

    /// <summary>
    /// Есть ли временное отличие предпросмотра от полного грейда. Обход и
    /// соло нужны для сравнения слоёв, но не являются частью авторского look.
    /// </summary>
    public bool HasPreviewOverrides => Solo.HasValue || BypassMask != 0;

    public void ClearPreviewOverrides()
    {
        Solo = null;
        Array.Clear(_bypass, 0, _bypass.Length);
    }

    /// <summary>Слой считается, если он не обойдён и не выключен чужим соло.</summary>
    public bool IsActive(ColorGradeLayer layer)
    {
        if (!IsEnabled(layer))
        {
            return false;
        }

        if (Solo.HasValue)
        {
            return Solo.Value == layer;
        }

        return !_bypass[(int)layer];
    }

    public void ResetToLook()
    {
        EnabledMask = (1 << LayerCount) - 1;
        Transform = PostProcessLook.Grade.Transform;
        Exposure = PostProcessLook.ColorGrading.Exposure;
        Contrast = PostProcessLook.ColorGrading.Contrast;
        Pivot = 0.5f;
        Shadows = 0f;
        Highlights = 0f;
        Blacks = 0f;
        Whites = 0f;
        Toe = 0f;
        Shoulder = 0f;
        Saturation = PostProcessLook.ColorGrading.Saturation;
        CdlSaturation = 1f;
        Vibrance = 0f;
        Hue = 0f;
        Temperature = PostProcessLook.Grade.Temperature;
        Tint = PostProcessLook.Grade.Tint;
        Slope = PostProcessLook.Grade.Slope;
        Offset = PostProcessLook.Grade.Offset;
        Power = PostProcessLook.Grade.Power;
        PrimaryLift = Vector3.zero;
        PrimaryGamma = Vector3.one;
        PrimaryGain = Vector3.one;
        PrimaryOffset = Vector3.zero;
        PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
        HueVsSaturation = new Vector4(120f, 30f, 15f, 1f);
        HueVsHue = new Vector4(120f, 30f, 15f, 0f);
        HueVsLuminance = new Vector4(120f, 30f, 15f, 0f);
        LuminanceVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
        SaturationVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
        CdlMaster = new Vector3(1f, 0f, 1f);
        WhitePoint = PostProcessLook.Grade.WhitePoint;
        BlackPoint = 0f;
        InputWhitePoint = 1f;
        HighlightRecovery = 0f;
        GreyOut = PostProcessLook.Grade.GreyOut;
        CurveSlope = PostProcessLook.Grade.CurveSlope;
        ShoulderPower = PostProcessLook.Grade.ShoulderPower;
        ToePower = PostProcessLook.Grade.ToePower;
        ToeStops = PostProcessLook.Grade.ToeStops;
        PathToWhiteAmount = PostProcessLook.Grade.PathToWhiteAmount;
        PathToWhitePower = PostProcessLook.Grade.PathToWhitePower;
        MasterCurve.Reset();
        RedCurve.Reset();
        GreenCurve.Reset();
        BlueCurve.Reset();
        HueVsHueCurve.Reset();
        HueVsSaturationCurve.Reset();
        HueVsLuminanceCurve.Reset();
        LuminanceVsSaturationCurve.Reset();
        SaturationVsSaturationCurve.Reset();
        Qualifier.Reset();
        ClearLut();
        ColorManagement.InputColorSpace = ColorGradeColorSpace.Rec709;
        ColorManagement.WorkingColorSpace = ColorGradeColorSpace.Rec709;
        ColorManagement.OutputColorSpace = ColorGradeColorSpace.Rec709;
        ColorManagement.InputTransfer = ColorGradeTransferFunction.Linear;
        ColorManagement.OutputTransfer = ColorGradeTransferFunction.Srgb;
        ColorManagement.ReferenceMode = ColorGradeReferenceMode.SceneReferred;
        ColorManagement.DynamicRange = ColorGradeDynamicRangeMode.Sdr;

        ClearPreviewOverrides();
    }

    public void ResetLayer(ColorGradeLayer layer)
    {
        SetEnabled(layer, true);
        switch (layer)
        {
            case ColorGradeLayer.Exposure:
                Exposure = PostProcessLook.ColorGrading.Exposure;
                BlackPoint = 0f;
                InputWhitePoint = 1f;
                HighlightRecovery = 0f;
                break;
            case ColorGradeLayer.WhiteBalance:
                Temperature = PostProcessLook.Grade.Temperature;
                Tint = PostProcessLook.Grade.Tint;
                PrimaryLift = Vector3.zero;
                PrimaryGamma = Vector3.one;
                PrimaryGain = Vector3.one;
                PrimaryOffset = Vector3.zero;
                PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
                break;
            case ColorGradeLayer.Cdl:
                Slope = PostProcessLook.Grade.Slope;
                Offset = PostProcessLook.Grade.Offset;
                Power = PostProcessLook.Grade.Power;
                CdlSaturation = 1f;
                CdlMaster = new Vector3(1f, 0f, 1f);
                break;
            case ColorGradeLayer.Saturation:
                Saturation = PostProcessLook.ColorGrading.Saturation;
                Vibrance = 0f;
                Hue = 0f;
                HueVsSaturation = new Vector4(120f, 30f, 15f, 1f);
                HueVsHue = new Vector4(120f, 30f, 15f, 0f);
                HueVsLuminance = new Vector4(120f, 30f, 15f, 0f);
                LuminanceVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
                SaturationVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
                HueVsHueCurve.Reset();
                HueVsSaturationCurve.Reset();
                HueVsLuminanceCurve.Reset();
                LuminanceVsSaturationCurve.Reset();
                SaturationVsSaturationCurve.Reset();
                break;
            case ColorGradeLayer.Contrast:
                Contrast = PostProcessLook.ColorGrading.Contrast;
                Pivot = 0.5f;
                Shadows = 0f;
                Highlights = 0f;
                Blacks = 0f;
                Whites = 0f;
                Toe = 0f;
                Shoulder = 0f;
                break;
            case ColorGradeLayer.Curve:
                Transform = PostProcessLook.Grade.Transform;
                WhitePoint = PostProcessLook.Grade.WhitePoint;
                GreyOut = PostProcessLook.Grade.GreyOut;
                CurveSlope = PostProcessLook.Grade.CurveSlope;
                ShoulderPower = PostProcessLook.Grade.ShoulderPower;
                ToePower = PostProcessLook.Grade.ToePower;
                ToeStops = PostProcessLook.Grade.ToeStops;
                PathToWhiteAmount = PostProcessLook.Grade.PathToWhiteAmount;
                PathToWhitePower = PostProcessLook.Grade.PathToWhitePower;
                MasterCurve.Reset();
                RedCurve.Reset();
                GreenCurve.Reset();
                BlueCurve.Reset();
                Qualifier.Reset();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer), layer, null);
        }
    }

    /// <summary>
    /// Repairs disk-loaded or externally assigned values before they reach GPU
    /// parameters. NaN and infinity are replaced with authored defaults.
    /// </summary>
    public void Sanitize()
    {
        EnabledMask &= (1 << LayerCount) - 1;
        if (!Enum.IsDefined(typeof(DisplayTransform), Transform))
        {
            Transform = PostProcessLook.Grade.Transform;
        }

        Exposure = FiniteClamp(
            Exposure,
            ExposureHardMin,
            ExposureHardMax,
            PostProcessLook.ColorGrading.Exposure);
        Contrast = FiniteClamp(Contrast, ContrastMin, ContrastMax, PostProcessLook.ColorGrading.Contrast);
        Pivot = FiniteClamp(Pivot, PivotMin, PivotMax, 0.5f);
        Shadows = FiniteClamp(Shadows, TonalAdjustmentMin, TonalAdjustmentMax, 0f);
        Highlights = FiniteClamp(Highlights, TonalAdjustmentMin, TonalAdjustmentMax, 0f);
        Blacks = FiniteClamp(Blacks, TonalAdjustmentMin, TonalAdjustmentMax, 0f);
        Whites = FiniteClamp(Whites, TonalAdjustmentMin, TonalAdjustmentMax, 0f);
        Toe = FiniteClamp(Toe, 0f, 1f, 0f);
        Shoulder = FiniteClamp(Shoulder, 0f, 1f, 0f);
        Saturation = FiniteClamp(Saturation, SaturationMin, SaturationMax, PostProcessLook.ColorGrading.Saturation);
        CdlSaturation = FiniteClamp(CdlSaturation, CdlSaturationMin, CdlSaturationMax, 1f);
        Vibrance = FiniteClamp(Vibrance, VibranceMin, VibranceMax, 0f);
        Hue = FiniteClamp(Hue, HueMin, HueMax, 0f);
        Temperature = FiniteClamp(Temperature, TemperatureMin, TemperatureMax, PostProcessLook.Grade.Temperature);
        Tint = FiniteClamp(Tint, TemperatureMin, TemperatureMax, PostProcessLook.Grade.Tint);
        Slope = FiniteClamp(Slope, SlopeMin, SlopeMax, PostProcessLook.Grade.Slope);
        Offset = FiniteClamp(Offset, OffsetMin, OffsetMax, PostProcessLook.Grade.Offset);
        Power = FiniteClamp(Power, PowerMin, PowerMax, PostProcessLook.Grade.Power);
        PrimaryLift = FiniteClamp(PrimaryLift, OffsetMin, OffsetMax, Vector3.zero);
        PrimaryGamma = FiniteClamp(PrimaryGamma, PowerMin, PowerMax, Vector3.one);
        PrimaryGain = FiniteClamp(PrimaryGain, SlopeMin, SlopeMax, Vector3.one);
        PrimaryOffset = FiniteClamp(PrimaryOffset, OffsetMin, OffsetMax, Vector3.zero);
        PrimaryMaster = new Vector4(
            FiniteClamp(PrimaryMaster.x, OffsetMin, OffsetMax, 0f),
            FiniteClamp(PrimaryMaster.y, PowerMin, PowerMax, 1f),
            FiniteClamp(PrimaryMaster.z, SlopeMin, SlopeMax, 1f),
            FiniteClamp(PrimaryMaster.w, OffsetMin, OffsetMax, 0f));
        CdlMaster = new Vector3(
            FiniteClamp(CdlMaster.x, SlopeMin, SlopeMax, 1f),
            FiniteClamp(CdlMaster.y, OffsetMin, OffsetMax, 0f),
            FiniteClamp(CdlMaster.z, PowerMin, PowerMax, 1f));
        HueVsSaturation = new Vector4(
            FiniteClamp(HueVsSaturation.x, HueCenterMin, HueCenterMax, 120f),
            FiniteClamp(HueVsSaturation.y, HueWidthMin, HueWidthMax, 30f),
            FiniteClamp(HueVsSaturation.z, HueFeatherMin, HueFeatherMax, 15f),
            FiniteClamp(HueVsSaturation.w, HueSaturationMin, HueSaturationMax, 1f));
        HueVsHue = SanitizeHueRange(HueVsHue, 0f);
        HueVsLuminance = SanitizeHueRange(HueVsLuminance, 0f);
        LuminanceVsSaturation = SanitizeSaturationCurve(LuminanceVsSaturation);
        SaturationVsSaturation = SanitizeSaturationCurve(SaturationVsSaturation);
        WhitePoint = FiniteClamp(WhitePoint, WhitePointMin, WhitePointMax, PostProcessLook.Grade.WhitePoint);
        BlackPoint = FiniteClamp(BlackPoint, BlackPointMin, BlackPointMax, 0f);
        InputWhitePoint = FiniteClamp(InputWhitePoint, InputWhitePointMin, InputWhitePointMax, 1f);
        InputWhitePoint = Mathf.Max(InputWhitePoint, BlackPoint + 0.01f);
        HighlightRecovery = FiniteClamp(HighlightRecovery, HighlightRecoveryMin, HighlightRecoveryMax, 0f);
        GreyOut = FiniteClamp(GreyOut, GreyOutMin, GreyOutMax, PostProcessLook.Grade.GreyOut);
        CurveSlope = FiniteClamp(CurveSlope, CurveSlopeMin, CurveSlopeMax, PostProcessLook.Grade.CurveSlope);
        ShoulderPower = FiniteClamp(
            ShoulderPower,
            CurvePowerMin,
            CurvePowerMax,
            PostProcessLook.Grade.ShoulderPower);
        ToePower = FiniteClamp(ToePower, CurvePowerMin, CurvePowerMax, PostProcessLook.Grade.ToePower);
        ToeStops = FiniteClamp(ToeStops, ToeStopsMin, ToeStopsMax, PostProcessLook.Grade.ToeStops);
        PathToWhiteAmount = FiniteClamp(
            PathToWhiteAmount,
            PathToWhiteAmountMin,
            PathToWhiteAmountMax,
            PostProcessLook.Grade.PathToWhiteAmount);
        PathToWhitePower = FiniteClamp(
            PathToWhitePower,
            PathToWhitePowerMin,
            PathToWhitePowerMax,
            PostProcessLook.Grade.PathToWhitePower);
        MasterCurve.Sanitize();
        RedCurve.Sanitize();
        GreenCurve.Sanitize();
        BlueCurve.Sanitize();
        HueVsHueCurve.Sanitize();
        HueVsSaturationCurve.Sanitize();
        HueVsLuminanceCurve.Sanitize();
        LuminanceVsSaturationCurve.Sanitize();
        SaturationVsSaturationCurve.Sanitize();
        Qualifier.Sanitize();
        LutIntensity = FiniteClamp(LutIntensity, 0f, 1f, 0f);
        if (!Enum.IsDefined(typeof(ColorGradeLutColorSpace), LutColorSpace))
        {
            LutColorSpace = ColorGradeLutColorSpace.LinearRec709;
        }
        ColorManagement.Sanitize();

        if (Solo.HasValue && !Enum.IsDefined(typeof(ColorGradeLayer), Solo.Value))
        {
            Solo = null;
        }

        if (Solo.HasValue && !IsEnabled(Solo.Value))
        {
            Solo = null;
        }
    }

    /// <summary>Builds the full authored snapshot without temporary preview overrides.</summary>
    public ColorGradeSnapshot ToAuthoredSnapshot() => new ColorGradeSnapshot
    {
        EnabledMask = EnabledMask,
        Transform = Transform,
        Exposure = Exposure,
        CdlSaturation = CdlSaturation,
        WhitePoint = WhitePoint,
        Temperature = Temperature,
        Tint = Tint,
        Slope = Slope,
        Offset = Offset,
        Power = Power,
        PrimaryLift = PrimaryLift,
        PrimaryGamma = PrimaryGamma,
        PrimaryGain = PrimaryGain,
        PrimaryOffset = PrimaryOffset,
        PrimaryMaster = PrimaryMaster,
        HueVsSaturation = HueVsSaturation,
        HueVsHue = HueVsHue,
        HueVsLuminance = HueVsLuminance,
        LuminanceVsSaturation = LuminanceVsSaturation,
        SaturationVsSaturation = SaturationVsSaturation,
        Vibrance = Vibrance,
        Hue = Hue,
        CdlMaster = CdlMaster,
        BlackPoint = BlackPoint,
        InputWhitePoint = InputWhitePoint,
        HighlightRecovery = HighlightRecovery,
        Pivot = Pivot,
        Shadows = Shadows,
        Highlights = Highlights,
        Blacks = Blacks,
        Whites = Whites,
        Toe = Toe,
        Shoulder = Shoulder,
        GreyOut = GreyOut,
        CurveSlope = CurveSlope,
        ShoulderPower = ShoulderPower,
        ToePower = ToePower,
        ToeStops = ToeStops,
        PathToWhiteAmount = PathToWhiteAmount,
        PathToWhitePower = PathToWhitePower,
        MasterCurve = MasterCurve.Clone(),
        RedCurve = RedCurve.Clone(),
        GreenCurve = GreenCurve.Clone(),
        BlueCurve = BlueCurve.Clone(),
        HueVsHueCurve = HueVsHueCurve.Clone(),
        HueVsSaturationCurve = HueVsSaturationCurve.Clone(),
        HueVsLuminanceCurve = HueVsLuminanceCurve.Clone(),
        LuminanceVsSaturationCurve = LuminanceVsSaturationCurve.Clone(),
        SaturationVsSaturationCurve = SaturationVsSaturationCurve.Clone(),
        Qualifier = Qualifier.Clone(),
        Lut = Lut,
        LutIntensity = LutIntensity,
        LutColorSpace = LutColorSpace,
        ColorManagement = ColorManagement.Clone(),
    }.Sanitized();

    /// <summary>Builds the sanitized, layer-aware preview snapshot sent to the GPU.</summary>
    public ColorGradeSnapshot ToSnapshot()
    {
        ColorGradeSnapshot authored = ToAuthoredSnapshot();
        return authored with
        {
            Transform = IsActive(ColorGradeLayer.Curve)
                ? authored.Transform
                : DisplayTransform.None,
            Exposure = IsActive(ColorGradeLayer.Exposure) ? authored.Exposure : 0f,
            BlackPoint = IsActive(ColorGradeLayer.Exposure) ? authored.BlackPoint : 0f,
            InputWhitePoint = IsActive(ColorGradeLayer.Exposure)
                ? authored.InputWhitePoint
                : 1f,
            HighlightRecovery = IsActive(ColorGradeLayer.Exposure)
                ? authored.HighlightRecovery
                : 0f,
            CdlSaturation = IsActive(ColorGradeLayer.Cdl) ? authored.CdlSaturation : 1f,
            MasterCurve = IsActive(ColorGradeLayer.Curve)
                ? authored.MasterCurve
                : new ColorGradeCurve(),
            RedCurve = IsActive(ColorGradeLayer.Curve)
                ? authored.RedCurve
                : new ColorGradeCurve(),
            GreenCurve = IsActive(ColorGradeLayer.Curve)
                ? authored.GreenCurve
                : new ColorGradeCurve(),
            BlueCurve = IsActive(ColorGradeLayer.Curve)
                ? authored.BlueCurve
                : new ColorGradeCurve(),
            HueVsHueCurve = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsHueCurve
                : new ColorGradeCurve(),
            HueVsSaturationCurve = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsSaturationCurve
                : new ColorGradeCurve(),
            HueVsLuminanceCurve = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsLuminanceCurve
                : new ColorGradeCurve(),
            LuminanceVsSaturationCurve = IsActive(ColorGradeLayer.Saturation)
                ? authored.LuminanceVsSaturationCurve
                : new ColorGradeCurve(),
            SaturationVsSaturationCurve = IsActive(ColorGradeLayer.Saturation)
                ? authored.SaturationVsSaturationCurve
                : new ColorGradeCurve(),
            Temperature = IsActive(ColorGradeLayer.WhiteBalance) ? authored.Temperature : 0f,
            Tint = IsActive(ColorGradeLayer.WhiteBalance) ? authored.Tint : 0f,
            PrimaryLift = IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryLift
                : Vector3.zero,
            PrimaryGamma = IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGamma
                : Vector3.one,
            PrimaryGain = IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryGain
                : Vector3.one,
            PrimaryOffset = IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryOffset
                : Vector3.zero,
            PrimaryMaster = IsActive(ColorGradeLayer.WhiteBalance)
                ? authored.PrimaryMaster
                : new Vector4(0f, 1f, 1f, 0f),
            Slope = IsActive(ColorGradeLayer.Cdl) ? authored.Slope : Vector3.one,
            Offset = IsActive(ColorGradeLayer.Cdl) ? authored.Offset : Vector3.zero,
            Power = IsActive(ColorGradeLayer.Cdl) ? authored.Power : Vector3.one,
            CdlMaster = IsActive(ColorGradeLayer.Cdl) ? authored.CdlMaster : new Vector3(1f, 0f, 1f),
            Pivot = IsActive(ColorGradeLayer.Contrast) ? authored.Pivot : 0.5f,
            Shadows = IsActive(ColorGradeLayer.Contrast) ? authored.Shadows : 0f,
            Highlights = IsActive(ColorGradeLayer.Contrast) ? authored.Highlights : 0f,
            Blacks = IsActive(ColorGradeLayer.Contrast) ? authored.Blacks : 0f,
            Whites = IsActive(ColorGradeLayer.Contrast) ? authored.Whites : 0f,
            Toe = IsActive(ColorGradeLayer.Contrast) ? authored.Toe : 0f,
            Shoulder = IsActive(ColorGradeLayer.Contrast) ? authored.Shoulder : 0f,
            HueVsSaturation = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsSaturation
                : new Vector4(120f, 30f, 15f, 1f),
            HueVsHue = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsHue
                : new Vector4(120f, 30f, 15f, 0f),
            HueVsLuminance = IsActive(ColorGradeLayer.Saturation)
                ? authored.HueVsLuminance
                : new Vector4(120f, 30f, 15f, 0f),
            LuminanceVsSaturation = IsActive(ColorGradeLayer.Saturation)
                ? authored.LuminanceVsSaturation
                : new Vector4(0.5f, 0.25f, 0.1f, 1f),
            SaturationVsSaturation = IsActive(ColorGradeLayer.Saturation)
                ? authored.SaturationVsSaturation
                : new Vector4(0.5f, 0.25f, 0.1f, 1f),
            Vibrance = IsActive(ColorGradeLayer.Saturation) ? authored.Vibrance : 0f,
            Hue = IsActive(ColorGradeLayer.Saturation) ? authored.Hue : 0f,
        };
    }

    public float EffectiveExposure => IsActive(ColorGradeLayer.Exposure) ? Exposure : 0f;

    public float EffectiveContrast => IsActive(ColorGradeLayer.Contrast) ? Contrast : 0f;

    public float EffectiveSaturation => IsActive(ColorGradeLayer.Saturation) ? Saturation : 1f;

    public float EffectiveVibrance => IsActive(ColorGradeLayer.Saturation) ? Vibrance : 0f;

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);

    private static Vector3 FiniteClamp(Vector3 value, float minimum, float maximum, Vector3 fallback) =>
        new(
            FiniteClamp(value.x, minimum, maximum, fallback.x),
            FiniteClamp(value.y, minimum, maximum, fallback.y),
            FiniteClamp(value.z, minimum, maximum, fallback.z));

    private static Vector4 SanitizeHueRange(Vector4 value, float neutralAmount) => new(
        FiniteClamp(value.x, 0f, 360f, 120f),
        FiniteClamp(value.y, 0f, 180f, 30f),
        FiniteClamp(value.z, 0f, 180f, 15f),
        FiniteClamp(value.w, -180f, 180f, neutralAmount));

    private static Vector4 SanitizeSaturationCurve(Vector4 value) => new(
        FiniteClamp(value.x, 0f, 1f, 0.5f),
        FiniteClamp(value.y, 0f, 1f, 0.25f),
        FiniteClamp(value.z, 0f, 1f, 0.1f),
        FiniteClamp(value.w, 0f, 2f, 1f));

    private static void TrimHistory(Stack<ColorGradeSnapshot> history)
    {
        while (history.Count > 64)
        {
            ColorGradeSnapshot[] entries = history.ToArray();
            history.Clear();
            for (int index = entries.Length - 2; index >= 0; index--)
            {
                history.Push(entries[index]);
            }
        }
    }

    private void RestoreSnapshot(ColorGradeSnapshot snapshot)
    {
        snapshot = snapshot.Sanitized();
        EnabledMask = snapshot.EnabledMask;
        Transform = snapshot.Transform;
        Exposure = snapshot.Exposure;
        Contrast = snapshot.Contrast;
        Pivot = snapshot.Pivot;
        Shadows = snapshot.Shadows;
        Highlights = snapshot.Highlights;
        Blacks = snapshot.Blacks;
        Whites = snapshot.Whites;
        Toe = snapshot.Toe;
        Shoulder = snapshot.Shoulder;
        Saturation = snapshot.Saturation;
        CdlSaturation = snapshot.CdlSaturation;
        Vibrance = snapshot.Vibrance;
        Hue = snapshot.Hue;
        Temperature = snapshot.Temperature;
        Tint = snapshot.Tint;
        Slope = snapshot.Slope;
        Offset = snapshot.Offset;
        Power = snapshot.Power;
        PrimaryLift = snapshot.PrimaryLift;
        PrimaryGamma = snapshot.PrimaryGamma;
        PrimaryGain = snapshot.PrimaryGain;
        PrimaryOffset = snapshot.PrimaryOffset;
        PrimaryMaster = snapshot.PrimaryMaster;
        CdlMaster = snapshot.CdlMaster;
        HueVsSaturation = snapshot.HueVsSaturation;
        HueVsHue = snapshot.HueVsHue;
        HueVsLuminance = snapshot.HueVsLuminance;
        LuminanceVsSaturation = snapshot.LuminanceVsSaturation;
        SaturationVsSaturation = snapshot.SaturationVsSaturation;
        WhitePoint = snapshot.WhitePoint;
        BlackPoint = snapshot.BlackPoint;
        InputWhitePoint = snapshot.InputWhitePoint;
        HighlightRecovery = snapshot.HighlightRecovery;
        GreyOut = snapshot.GreyOut;
        CurveSlope = snapshot.CurveSlope;
        ShoulderPower = snapshot.ShoulderPower;
        ToePower = snapshot.ToePower;
        ToeStops = snapshot.ToeStops;
        PathToWhiteAmount = snapshot.PathToWhiteAmount;
        PathToWhitePower = snapshot.PathToWhitePower;
        CopyCurve(MasterCurve, snapshot.MasterCurve);
        CopyCurve(RedCurve, snapshot.RedCurve);
        CopyCurve(GreenCurve, snapshot.GreenCurve);
        CopyCurve(BlueCurve, snapshot.BlueCurve);
        CopyCurve(HueVsHueCurve, snapshot.HueVsHueCurve);
        CopyCurve(HueVsSaturationCurve, snapshot.HueVsSaturationCurve);
        CopyCurve(HueVsLuminanceCurve, snapshot.HueVsLuminanceCurve);
        CopyCurve(LuminanceVsSaturationCurve, snapshot.LuminanceVsSaturationCurve);
        CopyCurve(SaturationVsSaturationCurve, snapshot.SaturationVsSaturationCurve);
        CopyQualifier(Qualifier, snapshot.Qualifier);
        ColorManagement.InputColorSpace = snapshot.ColorManagement.InputColorSpace;
        ColorManagement.WorkingColorSpace = snapshot.ColorManagement.WorkingColorSpace;
        ColorManagement.OutputColorSpace = snapshot.ColorManagement.OutputColorSpace;
        ColorManagement.InputTransfer = snapshot.ColorManagement.InputTransfer;
        ColorManagement.OutputTransfer = snapshot.ColorManagement.OutputTransfer;
        ColorManagement.ReferenceMode = snapshot.ColorManagement.ReferenceMode;
        ColorManagement.DynamicRange = snapshot.ColorManagement.DynamicRange;
        if (snapshot.Lut == null)
        {
            ClearLut();
        }
        else
        {
            if (!ReferenceEquals(Lut, snapshot.Lut))
            {
                ClearLut();
                LoadLut(snapshot.Lut.Path, out _);
            }

            LutIntensity = snapshot.LutIntensity;
            LutColorSpace = snapshot.LutColorSpace;
        }

        Sanitize();
    }

    private static void CopyCurve(ColorGradeCurve target, ColorGradeCurve source)
    {
        target.Load(
            source.Points.Take(source.PointCount).ToArray(),
            (int)source.Interpolation);
    }

    private static void CopyQualifier(ColorGradeQualifier target, ColorGradeQualifier source)
    {
        target.Reset();
        target.Enabled = source.Enabled;
        target.Invert = source.Invert;
        target.HueCenter = source.HueCenter;
        target.HueWidth = source.HueWidth;
        target.HueSoftness = source.HueSoftness;
        target.SaturationCenter = source.SaturationCenter;
        target.SaturationWidth = source.SaturationWidth;
        target.SaturationSoftness = source.SaturationSoftness;
        target.LuminanceCenter = source.LuminanceCenter;
        target.LuminanceWidth = source.LuminanceWidth;
        target.LuminanceSoftness = source.LuminanceSoftness;
        target.HueShift = source.HueShift;
        target.Saturation = source.Saturation;
        target.Exposure = source.Exposure;
        target.Temperature = source.Temperature;
        target.Tint = source.Tint;
        target.Lift = source.Lift;
        target.Gamma = source.Gamma;
        target.Gain = source.Gain;
        foreach (float sample in source.HueSamples)
        {
            target.AddHueSample(sample);
        }
    }

    private static bool SnapshotsEqual(ColorGradeSnapshot left, ColorGradeSnapshot right)
    {
        return left.EnabledMask == right.EnabledMask &&
            left.Transform == right.Transform &&
            Approximately(left.Exposure, right.Exposure) &&
            Approximately(left.Contrast, right.Contrast) &&
            left.Pivot == right.Pivot &&
            left.Shadows == right.Shadows &&
            left.Highlights == right.Highlights &&
            left.Blacks == right.Blacks &&
            left.Whites == right.Whites &&
            left.Toe == right.Toe &&
            left.Shoulder == right.Shoulder &&
            left.Saturation == right.Saturation &&
            left.CdlSaturation == right.CdlSaturation &&
            left.Vibrance == right.Vibrance &&
            left.Hue == right.Hue &&
            left.Temperature == right.Temperature &&
            left.Tint == right.Tint &&
            left.Slope == right.Slope &&
            left.Offset == right.Offset &&
            left.Power == right.Power &&
            left.PrimaryLift == right.PrimaryLift &&
            left.PrimaryGamma == right.PrimaryGamma &&
            left.PrimaryGain == right.PrimaryGain &&
            left.PrimaryOffset == right.PrimaryOffset &&
            left.PrimaryMaster == right.PrimaryMaster &&
            left.CdlMaster == right.CdlMaster &&
            left.HueVsSaturation == right.HueVsSaturation &&
            left.HueVsHue == right.HueVsHue &&
            left.HueVsLuminance == right.HueVsLuminance &&
            left.LuminanceVsSaturation == right.LuminanceVsSaturation &&
            left.SaturationVsSaturation == right.SaturationVsSaturation &&
            left.WhitePoint == right.WhitePoint &&
            left.BlackPoint == right.BlackPoint &&
            left.InputWhitePoint == right.InputWhitePoint &&
            left.HighlightRecovery == right.HighlightRecovery &&
            left.GreyOut == right.GreyOut &&
            left.CurveSlope == right.CurveSlope &&
            left.ShoulderPower == right.ShoulderPower &&
            left.ToePower == right.ToePower &&
            left.ToeStops == right.ToeStops &&
            left.PathToWhiteAmount == right.PathToWhiteAmount &&
            left.PathToWhitePower == right.PathToWhitePower &&
            ReferenceEquals(left.Lut, right.Lut) &&
            left.LutIntensity == right.LutIntensity &&
            left.LutColorSpace == right.LutColorSpace &&
            left.ColorManagement.InputColorSpace == right.ColorManagement.InputColorSpace &&
            left.ColorManagement.WorkingColorSpace == right.ColorManagement.WorkingColorSpace &&
            left.ColorManagement.OutputColorSpace == right.ColorManagement.OutputColorSpace &&
            left.ColorManagement.InputTransfer == right.ColorManagement.InputTransfer &&
            left.ColorManagement.OutputTransfer == right.ColorManagement.OutputTransfer &&
            left.ColorManagement.ReferenceMode == right.ColorManagement.ReferenceMode &&
            left.ColorManagement.DynamicRange == right.ColorManagement.DynamicRange &&
            CurvesEqual(left.MasterCurve, right.MasterCurve) &&
            CurvesEqual(left.RedCurve, right.RedCurve) &&
            CurvesEqual(left.GreenCurve, right.GreenCurve) &&
            CurvesEqual(left.BlueCurve, right.BlueCurve) &&
            CurvesEqual(left.HueVsHueCurve, right.HueVsHueCurve) &&
            CurvesEqual(left.HueVsSaturationCurve, right.HueVsSaturationCurve) &&
            CurvesEqual(left.HueVsLuminanceCurve, right.HueVsLuminanceCurve) &&
            CurvesEqual(left.LuminanceVsSaturationCurve, right.LuminanceVsSaturationCurve) &&
            CurvesEqual(left.SaturationVsSaturationCurve, right.SaturationVsSaturationCurve) &&
            QualifiersEqual(left.Qualifier, right.Qualifier);
    }

    private static bool CurvesEqual(ColorGradeCurve left, ColorGradeCurve right) =>
        left.PointCount == right.PointCount &&
        left.Interpolation == right.Interpolation &&
        left.Points.SequenceEqual(right.Points);

    private static bool QualifiersEqual(ColorGradeQualifier left, ColorGradeQualifier right) =>
        left.Enabled == right.Enabled &&
        left.Invert == right.Invert &&
        left.HueCenter == right.HueCenter &&
        left.HueWidth == right.HueWidth &&
        left.HueSoftness == right.HueSoftness &&
        left.SaturationCenter == right.SaturationCenter &&
        left.SaturationWidth == right.SaturationWidth &&
        left.SaturationSoftness == right.SaturationSoftness &&
        left.LuminanceCenter == right.LuminanceCenter &&
        left.LuminanceWidth == right.LuminanceWidth &&
        left.LuminanceSoftness == right.LuminanceSoftness &&
        left.HueShift == right.HueShift &&
        left.Saturation == right.Saturation &&
        left.Exposure == right.Exposure &&
        left.Temperature == right.Temperature &&
        left.Tint == right.Tint &&
        left.Lift == right.Lift &&
        left.Gamma == right.Gamma &&
        left.Gain == right.Gain &&
        left.HueSamples.SequenceEqual(right.HueSamples);

    private static bool Approximately(float left, float right) =>
        Mathf.Abs(left - right) <= 1e-5f;

    public bool LoadLut(string path, out string error)
    {
        if (!ColorGradeCubeLut.TryLoad(path, out ColorGradeCubeLut? lut, out error) || lut == null)
        {
            return false;
        }

        Lut?.Dispose();
        Lut = lut;
        LutPath = path;
        LutIntensity = 1f;
        return true;
    }

    public void ClearLut()
    {
        Lut?.Dispose();
        Lut = null;
        LutPath = string.Empty;
        LutIntensity = 0f;
    }
}
