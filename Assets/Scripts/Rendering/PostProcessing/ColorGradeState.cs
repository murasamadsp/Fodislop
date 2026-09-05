#nullable enable

using System;
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
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float WhitePointMin = 0.25f;
    public const float WhitePointMax = 8f;
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

    public ColorGradeState()
    {
        ResetToLook();
    }

    public DisplayTransform Transform { get; set; }

    public float Exposure { get; set; }

    public float Contrast { get; set; }

    public float Saturation { get; set; }

    public float Temperature { get; set; }

    public float Tint { get; set; }

    public Vector3 Slope { get; set; }

    public Vector3 Offset { get; set; }

    public Vector3 Power { get; set; }

    public float WhitePoint { get; set; }

    public float GreyOut { get; set; }

    public float CurveSlope { get; set; }

    public float ShoulderPower { get; set; }

    public float ToePower { get; set; }

    public float ToeStops { get; set; }

    public float PathToWhiteAmount { get; set; }

    public float PathToWhitePower { get; set; }

    /// <summary>
    /// Единственный невыключенный слой, либо <c>null</c>. Соло — не то же
    /// самое, что обход всех прочих: выйти из соло надо одним движением, иначе
    /// набор обходов после него уже не восстановить.
    /// </summary>
    public ColorGradeLayer? Solo { get; set; }

    public bool IsBypassed(ColorGradeLayer layer) => _bypass[(int)layer];

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
        if (Solo.HasValue)
        {
            return Solo.Value == layer;
        }

        return !_bypass[(int)layer];
    }

    public void ResetToLook()
    {
        Transform = PostProcessLook.Grade.Transform;
        Exposure = PostProcessLook.ColorGrading.Exposure;
        Contrast = PostProcessLook.ColorGrading.Contrast;
        Saturation = PostProcessLook.ColorGrading.Saturation;
        Temperature = PostProcessLook.Grade.Temperature;
        Tint = PostProcessLook.Grade.Tint;
        Slope = PostProcessLook.Grade.Slope;
        Offset = PostProcessLook.Grade.Offset;
        Power = PostProcessLook.Grade.Power;
        WhitePoint = PostProcessLook.Grade.WhitePoint;
        GreyOut = PostProcessLook.Grade.GreyOut;
        CurveSlope = PostProcessLook.Grade.CurveSlope;
        ShoulderPower = PostProcessLook.Grade.ShoulderPower;
        ToePower = PostProcessLook.Grade.ToePower;
        ToeStops = PostProcessLook.Grade.ToeStops;
        PathToWhiteAmount = PostProcessLook.Grade.PathToWhiteAmount;
        PathToWhitePower = PostProcessLook.Grade.PathToWhitePower;

        ClearPreviewOverrides();
    }

    public void ResetLayer(ColorGradeLayer layer)
    {
        switch (layer)
        {
            case ColorGradeLayer.Exposure:
                Exposure = PostProcessLook.ColorGrading.Exposure;
                break;
            case ColorGradeLayer.WhiteBalance:
                Temperature = PostProcessLook.Grade.Temperature;
                Tint = PostProcessLook.Grade.Tint;
                break;
            case ColorGradeLayer.Cdl:
                Slope = PostProcessLook.Grade.Slope;
                Offset = PostProcessLook.Grade.Offset;
                Power = PostProcessLook.Grade.Power;
                break;
            case ColorGradeLayer.Saturation:
                Saturation = PostProcessLook.ColorGrading.Saturation;
                break;
            case ColorGradeLayer.Contrast:
                Contrast = PostProcessLook.ColorGrading.Contrast;
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
        if (!Enum.IsDefined(typeof(DisplayTransform), Transform))
        {
            Transform = PostProcessLook.Grade.Transform;
        }

        Exposure = FiniteClamp(Exposure, ExposureMin, ExposureMax, PostProcessLook.ColorGrading.Exposure);
        Contrast = FiniteClamp(Contrast, ContrastMin, ContrastMax, PostProcessLook.ColorGrading.Contrast);
        Saturation = FiniteClamp(Saturation, SaturationMin, SaturationMax, PostProcessLook.ColorGrading.Saturation);
        Temperature = FiniteClamp(Temperature, TemperatureMin, TemperatureMax, PostProcessLook.Grade.Temperature);
        Tint = FiniteClamp(Tint, TemperatureMin, TemperatureMax, PostProcessLook.Grade.Tint);
        Slope = FiniteClamp(Slope, SlopeMin, SlopeMax, PostProcessLook.Grade.Slope);
        Offset = FiniteClamp(Offset, OffsetMin, OffsetMax, PostProcessLook.Grade.Offset);
        Power = FiniteClamp(Power, PowerMin, PowerMax, PostProcessLook.Grade.Power);
        WhitePoint = FiniteClamp(WhitePoint, WhitePointMin, WhitePointMax, PostProcessLook.Grade.WhitePoint);
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

        if (Solo.HasValue && !Enum.IsDefined(typeof(ColorGradeLayer), Solo.Value))
        {
            Solo = null;
        }
    }

    /// <summary>Builds the full authored snapshot without temporary preview overrides.</summary>
    public ColorGradeSnapshot ToAuthoredSnapshot() => new ColorGradeSnapshot
    {
        Transform = Transform,
        WhitePoint = WhitePoint,
        Temperature = Temperature,
        Tint = Tint,
        Slope = Slope,
        Offset = Offset,
        Power = Power,
        GreyOut = GreyOut,
        CurveSlope = CurveSlope,
        ShoulderPower = ShoulderPower,
        ToePower = ToePower,
        ToeStops = ToeStops,
        PathToWhiteAmount = PathToWhiteAmount,
        PathToWhitePower = PathToWhitePower,
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
            Temperature = IsActive(ColorGradeLayer.WhiteBalance) ? authored.Temperature : 0f,
            Tint = IsActive(ColorGradeLayer.WhiteBalance) ? authored.Tint : 0f,
            Slope = IsActive(ColorGradeLayer.Cdl) ? authored.Slope : Vector3.one,
            Offset = IsActive(ColorGradeLayer.Cdl) ? authored.Offset : Vector3.zero,
            Power = IsActive(ColorGradeLayer.Cdl) ? authored.Power : Vector3.one,
        };
    }

    public float EffectiveExposure => IsActive(ColorGradeLayer.Exposure) ? Exposure : 0f;

    public float EffectiveContrast => IsActive(ColorGradeLayer.Contrast) ? Contrast : 0f;

    public float EffectiveSaturation => IsActive(ColorGradeLayer.Saturation) ? Saturation : 1f;

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
