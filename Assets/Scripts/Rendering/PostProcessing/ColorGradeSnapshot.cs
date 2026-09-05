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
    /// <summary>Кривая вывода.</summary>
    public DisplayTransform Transform { get; init; }

    /// <summary>
    /// Сцен-линейная яркость, которая станет белым на дисплее. Делитель внутри
    /// финальной кривой вывода: чем он больше, тем больше света помещается в
    /// кадр и тем темнее середина.
    /// </summary>
    public float WhitePoint { get; init; }

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

    /// <summary>
    /// Нейтральный грейд с авторской кривой: точный no-op во всём, кроме
    /// самого сжатия диапазона.
    /// </summary>
    public static ColorGradeSnapshot FromLook() => new()
    {
        Transform = PostProcessLook.Grade.Transform,
        WhitePoint = PostProcessLook.Grade.WhitePoint,
        Temperature = PostProcessLook.Grade.Temperature,
        Tint = PostProcessLook.Grade.Tint,
        Slope = PostProcessLook.Grade.Slope,
        Offset = PostProcessLook.Grade.Offset,
        Power = PostProcessLook.Grade.Power,
        GreyOut = PostProcessLook.Grade.GreyOut,
        CurveSlope = PostProcessLook.Grade.CurveSlope,
        ShoulderPower = PostProcessLook.Grade.ShoulderPower,
        ToePower = PostProcessLook.Grade.ToePower,
        ToeStops = PostProcessLook.Grade.ToeStops,
        PathToWhiteAmount = PostProcessLook.Grade.PathToWhiteAmount,
        PathToWhitePower = PostProcessLook.Grade.PathToWhitePower,
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
            Transform = t > 0.5f ? other.Transform : Transform,
            WhitePoint = Mathf.Lerp(WhitePoint, other.WhitePoint, t),
            Temperature = Mathf.Lerp(Temperature, other.Temperature, t),
            Tint = Mathf.Lerp(Tint, other.Tint, t),
            Slope = Vector3.Lerp(Slope, other.Slope, t),
            Offset = Vector3.Lerp(Offset, other.Offset, t),
            Power = Vector3.Lerp(Power, other.Power, t),
            GreyOut = Mathf.Lerp(GreyOut, other.GreyOut, t),
            CurveSlope = Mathf.Lerp(CurveSlope, other.CurveSlope, t),
            ShoulderPower = Mathf.Lerp(ShoulderPower, other.ShoulderPower, t),
            ToePower = Mathf.Lerp(ToePower, other.ToePower, t),
            ToeStops = Mathf.Lerp(ToeStops, other.ToeStops, t),
            PathToWhiteAmount = Mathf.Lerp(PathToWhiteAmount, other.PathToWhiteAmount, t),
            PathToWhitePower = Mathf.Lerp(PathToWhitePower, other.PathToWhitePower, t),
        };
    }

    public ColorGradeSnapshot Sanitized()
    {
        ColorGradeSnapshot defaults = FromLook();
        return new ColorGradeSnapshot
        {
            Transform = System.Enum.IsDefined(typeof(DisplayTransform), Transform)
                ? Transform
                : defaults.Transform,
            WhitePoint = FiniteClamp(
                WhitePoint,
                ColorGradeState.WhitePointMin,
                ColorGradeState.WhitePointMax,
                defaults.WhitePoint),
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
        };
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
}
