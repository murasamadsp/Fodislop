#nullable enable

using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Слои цветового конвейера одним значением: то, что уходит в шейдер за кадр.
/// </summary>
/// <remarks>
/// Снимок, а не набор свойств прохода: проход принадлежит renderer asset, а не
/// сцене, инъекции в него нет, и состояние в него толкают снаружи — ровно так
/// же, как <see cref="AdvancedPostProcessSnapshot"/>. Одно значение вместо
/// десяти сеттеров означает, что кадр не может увидеть половину правки.
///
/// Величины здесь описывают ВИД и потому берутся из
/// <see cref="PostProcessLook.Grade"/>, а не из настроек игрока.
/// </remarks>
public readonly record struct ColorGradeSnapshot
{
    /// <summary>Bitmask of authored enabled grading layers.</summary>
    public int EnabledMask { get; init; }

    /// <summary>Кривая вывода.</summary>
    public DisplayTransform Transform { get; init; }

    /// <summary>Экспозиция в EV/stops. Ноль — нейтрально.</summary>
    public float Exposure { get; init; }

    /// <summary>Контраст относительно Pivot. Ноль — нейтрально.</summary>
    public float Contrast { get; init; }

    /// <summary>
    /// Сцен-линейная яркость, которая станет белым на дисплее. Делитель внутри
    /// финальной кривой вывода: чем он больше, тем больше света помещается в
    /// кадр и тем темнее середина.
    /// </summary>
    public float WhitePoint { get; init; }

    /// <summary>Входная точка чёрного HDR-сигнала. Ноль — neutral.</summary>
    public float BlackPoint { get; init; }

    /// <summary>Входная точка белого HDR-сигнала. Единица — neutral.</summary>
    public float InputWhitePoint { get; init; }

    /// <summary>Мягкое восстановление деталей выше входного white point.</summary>
    public float HighlightRecovery { get; init; }

    /// <summary>Сдвиг температуры в [-100, 100]. Ноль — нейтрально.</summary>
    public float Temperature { get; init; }

    /// <summary>Сдвиг оттенка зелёный/пурпурный в [-100, 100].</summary>
    public float Tint { get; init; }

    /// <summary>ASC CDL: усиление по каналам. Единица — нейтрально.</summary>
    public Vector3 Slope { get; init; }

    /// <summary>ASC CDL: подъём по каналам. Ноль — нейтрально.</summary>
    public Vector3 Offset { get; init; }

    /// <summary>ASC CDL: гамма средних тонов по каналам. Единица — нейтрально.</summary>
    public Vector3 Power { get; init; }

    public Vector3 PrimaryLift { get; init; }

    public Vector3 PrimaryGamma { get; init; }

    public Vector3 PrimaryGain { get; init; }

    public Vector3 PrimaryOffset { get; init; }

    public Vector4 PrimaryMaster { get; init; }

    public Vector3 CdlMaster { get; init; }

    /// <summary>Hue vs Saturation: центр, ширина, feather и multiplier.</summary>
    public Vector4 HueVsSaturation { get; init; }

    public Vector4 HueVsHue { get; init; }

    public Vector4 HueVsLuminance { get; init; }
    public Vector4 LuminanceVsSaturation { get; init; }
    public Vector4 SaturationVsSaturation { get; init; }

    /// <summary>Интеллектуальная насыщенность: сильнее действует на слабые цвета.</summary>
    public float Vibrance { get; init; }

    /// <summary>Глобальная насыщенность. Единица — нейтрально.</summary>
    public float Saturation { get; init; }

    public float CdlSaturation { get; init; }

    public float Hue { get; init; }

    public float Pivot { get; init; }
    public float Shadows { get; init; }
    public float Highlights { get; init; }
    public float Blacks { get; init; }
    public float Whites { get; init; }
    public float Toe { get; init; }
    public float Shoulder { get; init; }

    /// <summary>Значение дисплея, в которое ложится средне-серый сцены (0.18).</summary>
    public float GreyOut { get; init; }

    /// <summary>Наклон кривой в точке серого — контраст. Единица нейтральна.</summary>
    public float CurveSlope { get; init; }

    /// <summary>Резкость плеча. Ниже трёх кадр молочный.</summary>
    public float ShoulderPower { get; init; }

    /// <summary>Резкость носка. Держится ниже плеча.</summary>
    public float ToePower { get; init; }

    /// <summary>Стопов выхода под тени. Больше — глубже чёрное.</summary>
    public float ToeStops { get; init; }

    /// <summary>Сила ухода в белое у светов.</summary>
    public float PathToWhiteAmount { get; init; }

    /// <summary>Степень, с которой уход в белое набирает силу от яркости.</summary>
    public float PathToWhitePower { get; init; }

    public ColorGradeCurve MasterCurve { get; init; }

    public ColorGradeCurve RedCurve { get; init; }

    public ColorGradeCurve GreenCurve { get; init; }

    public ColorGradeCurve BlueCurve { get; init; }

    public ColorGradeCurve HueVsHueCurve { get; init; }

    public ColorGradeCurve HueVsSaturationCurve { get; init; }

    public ColorGradeCurve HueVsLuminanceCurve { get; init; }

    public ColorGradeCurve LuminanceVsSaturationCurve { get; init; }

    public ColorGradeCurve SaturationVsSaturationCurve { get; init; }

    public ColorGradeQualifier Qualifier { get; init; }

    public ColorGradeCubeLut? Lut { get; init; }

    public float LutIntensity { get; init; }

    public ColorGradeLutColorSpace LutColorSpace { get; init; }

    public ColorGradeColorManagement ColorManagement { get; init; }

    public ColorGradeSnapshot()
    {
        EnabledMask = (1 << 6) - 1;
        Contrast = 0f;
        Slope = Vector3.one;
        Offset = Vector3.zero;
        Power = Vector3.one;
        WhitePoint = PostProcessLook.Grade.WhitePoint;
        BlackPoint = 0f;
        InputWhitePoint = 1f;
        PrimaryLift = Vector3.zero;
        PrimaryGamma = Vector3.one;
        PrimaryGain = Vector3.one;
        PrimaryOffset = Vector3.zero;
        PrimaryMaster = new Vector4(0f, 1f, 1f, 0f);
        CdlMaster = new Vector3(1f, 0f, 1f);
        CdlSaturation = 1f;
        Saturation = PostProcessLook.ColorGrading.Saturation;
        Pivot = 0.5f;
        GreyOut = PostProcessLook.Grade.GreyOut;
        CurveSlope = PostProcessLook.Grade.CurveSlope;
        ShoulderPower = PostProcessLook.Grade.ShoulderPower;
        ToePower = PostProcessLook.Grade.ToePower;
        ToeStops = PostProcessLook.Grade.ToeStops;
        PathToWhitePower = PostProcessLook.Grade.PathToWhitePower;
        HueVsSaturation = new Vector4(120f, 30f, 15f, 1f);
        HueVsHue = new Vector4(120f, 30f, 15f, 0f);
        HueVsLuminance = new Vector4(120f, 30f, 15f, 0f);
        LuminanceVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
        SaturationVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f);
        MasterCurve = new ColorGradeCurve();
        RedCurve = new ColorGradeCurve();
        GreenCurve = new ColorGradeCurve();
        BlueCurve = new ColorGradeCurve();
        HueVsHueCurve = new ColorGradeCurve();
        HueVsSaturationCurve = new ColorGradeCurve();
        HueVsLuminanceCurve = new ColorGradeCurve();
        LuminanceVsSaturationCurve = new ColorGradeCurve();
        SaturationVsSaturationCurve = new ColorGradeCurve();
        Qualifier = new ColorGradeQualifier();
        ColorManagement = new ColorGradeColorManagement();
    }

    /// <summary>
    /// Нейтральный грейд с авторской кривой: точный no-op во всём, кроме
    /// самого сжатия диапазона.
    /// </summary>
    public static ColorGradeSnapshot FromLook() => new()
    {
        Transform = PostProcessLook.Grade.Transform,
        Exposure = PostProcessLook.ColorGrading.Exposure,
        Contrast = PostProcessLook.ColorGrading.Contrast,
        WhitePoint = PostProcessLook.Grade.WhitePoint,
        BlackPoint = 0f,
        InputWhitePoint = 1f,
        HighlightRecovery = 0f,
        Pivot = 0.5f,
        Shadows = 0f,
        Highlights = 0f,
        Blacks = 0f,
        Whites = 0f,
        Toe = 0f,
        Shoulder = 0f,
        Temperature = PostProcessLook.Grade.Temperature,
        Tint = PostProcessLook.Grade.Tint,
        Slope = PostProcessLook.Grade.Slope,
        Offset = PostProcessLook.Grade.Offset,
        Power = PostProcessLook.Grade.Power,
        PrimaryLift = Vector3.zero,
        PrimaryGamma = Vector3.one,
        PrimaryGain = Vector3.one,
        PrimaryOffset = Vector3.zero,
        PrimaryMaster = new Vector4(0f, 1f, 1f, 0f),
        HueVsSaturation = new Vector4(120f, 30f, 15f, 1f),
        HueVsHue = new Vector4(120f, 30f, 15f, 0f),
        HueVsLuminance = new Vector4(120f, 30f, 15f, 0f),
        LuminanceVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f),
        SaturationVsSaturation = new Vector4(0.5f, 0.25f, 0.1f, 1f),
        Vibrance = 0f,
        Saturation = PostProcessLook.ColorGrading.Saturation,
        CdlSaturation = 1f,
        Hue = 0f,
        CdlMaster = new Vector3(1f, 0f, 1f),
        GreyOut = PostProcessLook.Grade.GreyOut,
        CurveSlope = PostProcessLook.Grade.CurveSlope,
        ShoulderPower = PostProcessLook.Grade.ShoulderPower,
        ToePower = PostProcessLook.Grade.ToePower,
        ToeStops = PostProcessLook.Grade.ToeStops,
        PathToWhiteAmount = PostProcessLook.Grade.PathToWhiteAmount,
        PathToWhitePower = PostProcessLook.Grade.PathToWhitePower,
        MasterCurve = new ColorGradeCurve(),
        RedCurve = new ColorGradeCurve(),
        GreenCurve = new ColorGradeCurve(),
        BlueCurve = new ColorGradeCurve(),
        Qualifier = new ColorGradeQualifier(),
        LutIntensity = 0f,
        LutColorSpace = ColorGradeLutColorSpace.LinearRec709,
        ColorManagement = new ColorGradeColorManagement(),
    };

    [ExcludeFromCodeCoverage]
    public ColorGradeSnapshot WithTemperature(float temperature) => this with
    {
        Temperature = temperature,
    };

    /// <summary>Смешивает два грейда: <c>0</c> — этот, <c>1</c> — <paramref name="other"/>.</summary>
    /// <remarks>
    /// Кривая вывода НЕ смешивается — она берётся у того снимка, чей вес больше
    /// половины. Между «есть кривая» и «нет кривой» нет промежуточных
    /// состояний: половина сжатия диапазона — это не мягкий переход, а просто
    /// неверный кадр. Всё остальное — величины, и они смешиваются линейно.
    /// </remarks>
    public ColorGradeSnapshot BlendTo(ColorGradeSnapshot other, float weight)
    {
        float t = float.IsNaN(weight) ? 0f : Mathf.Clamp01(weight);
        if (t <= 0f)
        {
            return this;
        }

        if (t >= 1f)
        {
            return other;
        }

        return new ColorGradeSnapshot
        {
            EnabledMask = t > 0.5f ? other.EnabledMask : EnabledMask,
            Transform = t > 0.5f ? other.Transform : Transform,
            Exposure = Mathf.Lerp(Exposure, other.Exposure, t),
            Contrast = Mathf.Lerp(Contrast, other.Contrast, t),
            WhitePoint = Mathf.Lerp(WhitePoint, other.WhitePoint, t),
            BlackPoint = Mathf.Lerp(BlackPoint, other.BlackPoint, t),
            InputWhitePoint = Mathf.Lerp(InputWhitePoint, other.InputWhitePoint, t),
            HighlightRecovery = Mathf.Lerp(HighlightRecovery, other.HighlightRecovery, t),
            Pivot = Mathf.Lerp(Pivot, other.Pivot, t),
            Shadows = Mathf.Lerp(Shadows, other.Shadows, t),
            Highlights = Mathf.Lerp(Highlights, other.Highlights, t),
            Blacks = Mathf.Lerp(Blacks, other.Blacks, t),
            Whites = Mathf.Lerp(Whites, other.Whites, t),
            Toe = Mathf.Lerp(Toe, other.Toe, t),
            Shoulder = Mathf.Lerp(Shoulder, other.Shoulder, t),
            Temperature = Mathf.Lerp(Temperature, other.Temperature, t),
            Tint = Mathf.Lerp(Tint, other.Tint, t),
            Slope = Vector3.Lerp(Slope, other.Slope, t),
            Offset = Vector3.Lerp(Offset, other.Offset, t),
            Power = Vector3.Lerp(Power, other.Power, t),
            PrimaryLift = Vector3.Lerp(PrimaryLift, other.PrimaryLift, t),
            PrimaryGamma = Vector3.Lerp(PrimaryGamma, other.PrimaryGamma, t),
            PrimaryGain = Vector3.Lerp(PrimaryGain, other.PrimaryGain, t),
            PrimaryOffset = Vector3.Lerp(PrimaryOffset, other.PrimaryOffset, t),
            PrimaryMaster = Vector4.Lerp(PrimaryMaster, other.PrimaryMaster, t),
            HueVsSaturation = Vector4.Lerp(HueVsSaturation, other.HueVsSaturation, t),
            HueVsHue = Vector4.Lerp(HueVsHue, other.HueVsHue, t),
            HueVsLuminance = Vector4.Lerp(HueVsLuminance, other.HueVsLuminance, t),
            LuminanceVsSaturation = Vector4.Lerp(LuminanceVsSaturation, other.LuminanceVsSaturation, t),
            SaturationVsSaturation = Vector4.Lerp(SaturationVsSaturation, other.SaturationVsSaturation, t),
            Vibrance = Mathf.Lerp(Vibrance, other.Vibrance, t),
            Saturation = Mathf.Lerp(Saturation, other.Saturation, t),
            CdlSaturation = Mathf.Lerp(CdlSaturation, other.CdlSaturation, t),
            Hue = Mathf.Lerp(Hue, other.Hue, t),
            CdlMaster = Vector3.Lerp(CdlMaster, other.CdlMaster, t),
            GreyOut = Mathf.Lerp(GreyOut, other.GreyOut, t),
            CurveSlope = Mathf.Lerp(CurveSlope, other.CurveSlope, t),
            ShoulderPower = Mathf.Lerp(ShoulderPower, other.ShoulderPower, t),
            ToePower = Mathf.Lerp(ToePower, other.ToePower, t),
            ToeStops = Mathf.Lerp(ToeStops, other.ToeStops, t),
            PathToWhiteAmount = Mathf.Lerp(PathToWhiteAmount, other.PathToWhiteAmount, t),
            PathToWhitePower = Mathf.Lerp(PathToWhitePower, other.PathToWhitePower, t),
            MasterCurve = t > 0.5f ? other.MasterCurve.Clone() : MasterCurve.Clone(),
            RedCurve = t > 0.5f ? other.RedCurve.Clone() : RedCurve.Clone(),
            GreenCurve = t > 0.5f ? other.GreenCurve.Clone() : GreenCurve.Clone(),
            BlueCurve = t > 0.5f ? other.BlueCurve.Clone() : BlueCurve.Clone(),
            HueVsHueCurve = t > 0.5f ? other.HueVsHueCurve.Clone() : HueVsHueCurve.Clone(),
            HueVsSaturationCurve = t > 0.5f
                ? other.HueVsSaturationCurve.Clone()
                : HueVsSaturationCurve.Clone(),
            HueVsLuminanceCurve = t > 0.5f
                ? other.HueVsLuminanceCurve.Clone()
                : HueVsLuminanceCurve.Clone(),
            LuminanceVsSaturationCurve = t > 0.5f
                ? other.LuminanceVsSaturationCurve.Clone()
                : LuminanceVsSaturationCurve.Clone(),
            SaturationVsSaturationCurve = t > 0.5f
                ? other.SaturationVsSaturationCurve.Clone()
                : SaturationVsSaturationCurve.Clone(),
            Qualifier = t > 0.5f ? other.Qualifier.Clone() : Qualifier.Clone(),
            Lut = t > 0.5f ? other.Lut : Lut,
            LutIntensity = Mathf.Lerp(LutIntensity, other.LutIntensity, t),
            LutColorSpace = t > 0.5f ? other.LutColorSpace : LutColorSpace,
            ColorManagement = t > 0.5f
                ? other.ColorManagement.Clone()
                : ColorManagement.Clone(),
        };
    }

    public ColorGradeSnapshot Sanitized()
    {
        ColorGradeSnapshot defaults = FromLook();
        return new ColorGradeSnapshot
        {
            EnabledMask = EnabledMask & ((1 << 6) - 1),
            Transform = System.Enum.IsDefined(typeof(DisplayTransform), Transform)
                ? Transform
                : defaults.Transform,
            Exposure = FiniteClamp(
                Exposure,
                ColorGradeState.ExposureHardMin,
                ColorGradeState.ExposureHardMax,
                defaults.Exposure),
            WhitePoint = FiniteClamp(
                WhitePoint,
                ColorGradeState.WhitePointMin,
                ColorGradeState.WhitePointMax,
                defaults.WhitePoint),
            BlackPoint = FiniteClamp(BlackPoint, 0f, 0.99f, defaults.BlackPoint),
            InputWhitePoint = Mathf.Max(
                FiniteClamp(InputWhitePoint, 0.01f, 64f, defaults.InputWhitePoint),
                FiniteClamp(BlackPoint, 0f, 0.99f, defaults.BlackPoint) + 0.01f),
            HighlightRecovery = FiniteClamp(HighlightRecovery, 0f, 1f, defaults.HighlightRecovery),
            Contrast = FiniteClamp(
                Contrast,
                ColorGradeState.ContrastMin,
                ColorGradeState.ContrastMax,
                defaults.Contrast),
            Pivot = FiniteClamp(Pivot, 0.1f, 0.9f, defaults.Pivot),
            Shadows = FiniteClamp(Shadows, -0.5f, 0.5f, defaults.Shadows),
            Highlights = FiniteClamp(Highlights, -0.5f, 0.5f, defaults.Highlights),
            Blacks = FiniteClamp(Blacks, -0.5f, 0.5f, defaults.Blacks),
            Whites = FiniteClamp(Whites, -0.5f, 0.5f, defaults.Whites),
            Toe = FiniteClamp(Toe, 0f, 1f, defaults.Toe),
            Shoulder = FiniteClamp(Shoulder, 0f, 1f, defaults.Shoulder),
            Temperature = FiniteClamp(
                Temperature,
                ColorGradeState.TemperatureMin,
                ColorGradeState.TemperatureMax,
                defaults.Temperature),
            Tint = FiniteClamp(
                Tint,
                ColorGradeState.TemperatureMin,
                ColorGradeState.TemperatureMax,
                defaults.Tint),
            Slope = FiniteClamp(
                Slope,
                ColorGradeState.SlopeMin,
                ColorGradeState.SlopeMax,
                defaults.Slope),
            Offset = FiniteClamp(
                Offset,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.Offset),
            Power = FiniteClamp(
                Power,
                ColorGradeState.PowerMin,
                ColorGradeState.PowerMax,
                defaults.Power),
            PrimaryLift = FiniteClamp(
                PrimaryLift,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.PrimaryLift),
            PrimaryGamma = FiniteClamp(
                PrimaryGamma,
                ColorGradeState.PowerMin,
                ColorGradeState.PowerMax,
                defaults.PrimaryGamma),
            PrimaryGain = FiniteClamp(
                PrimaryGain,
                ColorGradeState.SlopeMin,
                ColorGradeState.SlopeMax,
                defaults.PrimaryGain),
            PrimaryOffset = FiniteClamp(
                PrimaryOffset,
                ColorGradeState.OffsetMin,
                ColorGradeState.OffsetMax,
                defaults.PrimaryOffset),
            PrimaryMaster = new Vector4(
                FiniteClamp(PrimaryMaster.x, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f),
                FiniteClamp(PrimaryMaster.y, ColorGradeState.PowerMin, ColorGradeState.PowerMax, 1f),
                FiniteClamp(PrimaryMaster.z, ColorGradeState.SlopeMin, ColorGradeState.SlopeMax, 1f),
                FiniteClamp(PrimaryMaster.w, ColorGradeState.OffsetMin, ColorGradeState.OffsetMax, 0f)),
            HueVsSaturation = SanitizeHueVsSaturation(HueVsSaturation, defaults.HueVsSaturation),
            HueVsHue = SanitizeHueRange(HueVsHue, defaults.HueVsHue),
            HueVsLuminance = SanitizeHueRange(HueVsLuminance, defaults.HueVsLuminance),
            LuminanceVsSaturation = SanitizeSaturationCurve(LuminanceVsSaturation, defaults.LuminanceVsSaturation),
            SaturationVsSaturation = SanitizeSaturationCurve(SaturationVsSaturation, defaults.SaturationVsSaturation),
            Vibrance = FiniteClamp(Vibrance, -1f, 1f, defaults.Vibrance),
            Saturation = FiniteClamp(
                Saturation,
                ColorGradeState.SaturationMin,
                ColorGradeState.SaturationMax,
                defaults.Saturation),
            CdlSaturation = FiniteClamp(
                CdlSaturation,
                ColorGradeState.CdlSaturationMin,
                ColorGradeState.CdlSaturationMax,
                defaults.CdlSaturation),
            Hue = FiniteClamp(Hue, -180f, 180f, defaults.Hue),
            CdlMaster = new Vector3(
                FiniteClamp(CdlMaster.x, 0f, 4f, defaults.CdlMaster.x),
                FiniteClamp(CdlMaster.y, -0.5f, 0.5f, defaults.CdlMaster.y),
                FiniteClamp(CdlMaster.z, 0.1f, 4f, defaults.CdlMaster.z)),
            GreyOut = FiniteClamp(
                GreyOut,
                ColorGradeState.GreyOutMin,
                ColorGradeState.GreyOutMax,
                defaults.GreyOut),
            CurveSlope = FiniteClamp(
                CurveSlope,
                ColorGradeState.CurveSlopeMin,
                ColorGradeState.CurveSlopeMax,
                defaults.CurveSlope),
            ShoulderPower = FiniteClamp(
                ShoulderPower,
                ColorGradeState.CurvePowerMin,
                ColorGradeState.CurvePowerMax,
                defaults.ShoulderPower),
            ToePower = FiniteClamp(
                ToePower,
                ColorGradeState.CurvePowerMin,
                ColorGradeState.CurvePowerMax,
                defaults.ToePower),
            ToeStops = FiniteClamp(
                ToeStops,
                ColorGradeState.ToeStopsMin,
                ColorGradeState.ToeStopsMax,
                defaults.ToeStops),
            PathToWhiteAmount = FiniteClamp(
                PathToWhiteAmount,
                ColorGradeState.PathToWhiteAmountMin,
                ColorGradeState.PathToWhiteAmountMax,
                defaults.PathToWhiteAmount),
            PathToWhitePower = FiniteClamp(
                PathToWhitePower,
                ColorGradeState.PathToWhitePowerMin,
                ColorGradeState.PathToWhitePowerMax,
                defaults.PathToWhitePower),
            MasterCurve = SanitizeCurve(MasterCurve, defaults.MasterCurve),
            RedCurve = SanitizeCurve(RedCurve, defaults.RedCurve),
            GreenCurve = SanitizeCurve(GreenCurve, defaults.GreenCurve),
            BlueCurve = SanitizeCurve(BlueCurve, defaults.BlueCurve),
            HueVsHueCurve = SanitizeCurve(HueVsHueCurve, defaults.HueVsHueCurve),
            HueVsSaturationCurve = SanitizeCurve(
                HueVsSaturationCurve,
                defaults.HueVsSaturationCurve),
            HueVsLuminanceCurve = SanitizeCurve(
                HueVsLuminanceCurve,
                defaults.HueVsLuminanceCurve),
            LuminanceVsSaturationCurve = SanitizeCurve(
                LuminanceVsSaturationCurve,
                defaults.LuminanceVsSaturationCurve),
            SaturationVsSaturationCurve = SanitizeCurve(
                SaturationVsSaturationCurve,
                defaults.SaturationVsSaturationCurve),
            Qualifier = SanitizeQualifier(Qualifier, defaults.Qualifier),
            Lut = LutIntensity > 0.0001f ? Lut : null,
            LutIntensity = FiniteClamp(LutIntensity, 0f, 1f, 0f),
            LutColorSpace = System.Enum.IsDefined(typeof(ColorGradeLutColorSpace), LutColorSpace)
                ? LutColorSpace
                : defaults.LutColorSpace,
            ColorManagement = SanitizeColorManagement(ColorManagement, defaults.ColorManagement),
        };
    }

    private static ColorGradeCurve SanitizeCurve(ColorGradeCurve? curve, ColorGradeCurve fallback)
    {
        ColorGradeCurve result = curve?.Clone() ?? fallback.Clone();
        result.Sanitize();
        return result;
    }

    private static ColorGradeQualifier SanitizeQualifier(
        ColorGradeQualifier? qualifier,
        ColorGradeQualifier fallback)
    {
        ColorGradeQualifier result = qualifier?.Clone() ?? fallback.Clone();
        result.Sanitize();
        return result;
    }

    private static ColorGradeColorManagement SanitizeColorManagement(
        ColorGradeColorManagement? settings,
        ColorGradeColorManagement fallback)
    {
        ColorGradeColorManagement result = settings?.Clone() ?? fallback.Clone();
        result.Sanitize();
        return result;
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);

    private static Vector3 FiniteClamp(Vector3 value, float minimum, float maximum, Vector3 fallback) =>
        new(
            FiniteClamp(value.x, minimum, maximum, fallback.x),
            FiniteClamp(value.y, minimum, maximum, fallback.y),
            FiniteClamp(value.z, minimum, maximum, fallback.z));

    private static Vector4 SanitizeHueVsSaturation(Vector4 value, Vector4 fallback) => new(
        FiniteClamp(value.x, 0f, 360f, fallback.x),
        FiniteClamp(value.y, 0f, 180f, fallback.y),
        FiniteClamp(value.z, 0f, 180f, fallback.z),
        FiniteClamp(value.w, 0f, 2f, fallback.w));

    private static Vector4 SanitizeHueRange(Vector4 value, Vector4 fallback) => new(
        FiniteClamp(value.x, 0f, 360f, fallback.x),
        FiniteClamp(value.y, 0f, 180f, fallback.y),
        FiniteClamp(value.z, 0f, 180f, fallback.z),
        FiniteClamp(value.w, -180f, 180f, fallback.w));

    private static Vector4 SanitizeSaturationCurve(Vector4 value, Vector4 fallback) => new(
        FiniteClamp(value.x, 0f, 1f, fallback.x),
        FiniteClamp(value.y, 0f, 1f, fallback.y),
        FiniteClamp(value.z, 0f, 1f, fallback.z),
        FiniteClamp(value.w, 0f, 2f, fallback.w));
}
