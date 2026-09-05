#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>Плоская форма зоны для <c>JsonUtility</c>.</summary>
/// <remarks>
/// Отдельный тип, а не сама <see cref="ColorGradeZone"/>: та — неизменяемая
/// запись со свойствами, а <c>JsonUtility</c> видит только публичные поля и
/// требует конструктор без аргументов. Плоская форма ещё и разводит два
/// срока жизни: вид может меняться свободно, формат файла — только со
/// сменой версии.
/// </remarks>
[Serializable]
internal sealed class ColorGradeZonePayload
{
    public string Name = string.Empty;
    public float CenterY;
    public float HalfHeight;
    public float Feather;
    public float Exposure = PostProcessLook.ColorGrading.Exposure;
    public float Contrast = PostProcessLook.ColorGrading.Contrast;
    public float Saturation = PostProcessLook.ColorGrading.Saturation;
    public int Transform;
    public float Temperature;
    public float Tint;
    public Vector3 Slope = Vector3.one;
    public Vector3 Offset;
    public Vector3 Power = Vector3.one;
    public float WhitePoint = 1f;
    public float GreyOut;
    public float CurveSlope;
    public float ShoulderPower;
    public float ToePower;
    public float ToeStops;
    public float PathToWhiteAmount;
    public float PathToWhitePower;
}

/// <summary>Перевод зон между рабочим видом и формой файла.</summary>
internal static class ColorGradeZonePayloads
{
    public static ColorGradeZonePayload[] From(ColorGradeZones? zones)
    {
        if (zones == null || zones.Count == 0)
        {
            return Array.Empty<ColorGradeZonePayload>();
        }

        var result = new ColorGradeZonePayload[zones.Count];
        for (int index = 0; index < zones.Count; index++)
        {
            ColorGradeZone zone = zones.Zones[index];
            ColorGradeSnapshot grade = zone.Grade;
            result[index] = new ColorGradeZonePayload
            {
                Name = zone.Name,
                CenterY = zone.CenterY,
                HalfHeight = zone.HalfHeight,
                Feather = zone.Feather,
                Exposure = zone.Exposure,
                Contrast = zone.Contrast,
                Saturation = zone.Saturation,
                Transform = (int)grade.Transform,
                Temperature = grade.Temperature,
                Tint = grade.Tint,
                Slope = grade.Slope,
                Offset = grade.Offset,
                Power = grade.Power,
                WhitePoint = grade.WhitePoint,
                GreyOut = grade.GreyOut,
                CurveSlope = grade.CurveSlope,
                ShoulderPower = grade.ShoulderPower,
                ToePower = grade.ToePower,
                ToeStops = grade.ToeStops,
                PathToWhiteAmount = grade.PathToWhiteAmount,
                PathToWhitePower = grade.PathToWhitePower,
            };
        }

        return result;
    }

    /// <summary>
    /// Заполняет набор зон прочитанным. Пустой или отсутствующий список
    /// очищает набор — это состояние «зон не объявлено», а не ошибка.
    /// </summary>
    public static void Into(
        ColorGradeZones? zones,
        bool enabled,
        ColorGradeZonePayload[]? payloads,
        int payloadVersion)
    {
        if (zones == null)
        {
            return;
        }

        zones.Clear();
        zones.Enabled = enabled;
        if (payloads == null)
        {
            return;
        }

        foreach (ColorGradeZonePayload payload in payloads)
        {
            if (payload == null)
            {
                continue;
            }

            bool hasFullGrade = payloadVersion >= 3;
            zones.Add(new ColorGradeZone
            {
                Name = payload.Name,
                CenterY = payload.CenterY,
                HalfHeight = payload.HalfHeight,
                Feather = payload.Feather,
                Exposure = hasFullGrade
                    ? payload.Exposure
                    : PostProcessLook.ColorGrading.Exposure,
                Contrast = hasFullGrade
                    ? payload.Contrast
                    : PostProcessLook.ColorGrading.Contrast,
                Saturation = hasFullGrade
                    ? payload.Saturation
                    : PostProcessLook.ColorGrading.Saturation,
                Grade = new ColorGradeSnapshot
                {
                    Transform = (DisplayTransform)payload.Transform,
                    Temperature = payload.Temperature,
                    Tint = payload.Tint,
                    Slope = payload.Slope,
                    Offset = payload.Offset,
                    Power = payload.Power,
                    WhitePoint = payload.WhitePoint,
                    GreyOut = payload.GreyOut,
                    CurveSlope = payload.CurveSlope,
                    ShoulderPower = payload.ShoulderPower,
                    ToePower = payload.ToePower,
                    ToeStops = payload.ToeStops,
                    PathToWhiteAmount = payload.PathToWhiteAmount,
                    PathToWhitePower = payload.PathToWhitePower,
                },
            });
        }
    }
}
