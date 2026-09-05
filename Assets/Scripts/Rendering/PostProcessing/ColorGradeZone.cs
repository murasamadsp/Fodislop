#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>
/// Грейд, привязанный к высоте в мире.
/// </summary>
/// <remarks>
/// ПОЧЕМУ ПО ВЫСОТЕ, А НЕ ПО ОБЪЁМАМ. Мир игры вытянут вниз: поверхность,
/// верхние слои, глубина. Место здесь — это глубина и почти ничего кроме,
/// поэтому одна координата описывает его полностью, а объёмы Volume
/// потребовали бы авторства в <c>.asset</c>, который правится только
/// редактором. Зона объявляется числом и живёт рядом с остальным видом.
///
/// ПОЧЕМУ ПЕРЕКРЫТИЕ, А НЕ ГРАНИЦА. Резкая граница читается как щелчок
/// цвета при пересечении: кадр меняется за один кадр, и это видно всегда.
/// Поэтому у зоны есть растушёвка, внутри которой её вес плавно растёт от
/// нуля до единицы, и соседние зоны складываются по весам.
/// </remarks>
public readonly record struct ColorGradeZone
{
    public ColorGradeZone(
        string name,
        float centerY,
        float halfHeight,
        float feather,
        ColorGradeSnapshot grade,
        float exposure = PostProcessLook.ColorGrading.Exposure,
        float contrast = PostProcessLook.ColorGrading.Contrast,
        float saturation = PostProcessLook.ColorGrading.Saturation,
        float centerX = 0f,
        float halfWidth = float.PositiveInfinity)
    {
        Name = name;
        CenterY = centerY;
        HalfHeight = halfHeight;
        Feather = feather;
        Exposure = exposure;
        Contrast = contrast;
        Saturation = saturation;
        Grade = grade;
        CenterX = centerX;
        HalfWidth = halfWidth;
    }

    /// <summary>Имя для инструмента автора. На вид не влияет.</summary>
    public string Name { get; init; }

    /// <summary>Мировая высота середины зоны.</summary>
    public float CenterY { get; init; }

    /// <summary>Полувысота ядра, где вес равен единице.</summary>
    public float HalfHeight { get; init; }

    /// <summary>Растушёвка снаружи ядра, где вес падает до нуля.</summary>
    public float Feather { get; init; }

    /// <summary>Мировая координата X середины зоны (для 2D-зон).</summary>
    public float CenterX { get; init; } = 0f;

    /// <summary>Полуширина ядра по X. Бесконечность — зона на всю ширину мира.</summary>
    public float HalfWidth { get; init; } = float.PositiveInfinity;

    /// <summary>Авторская экспозиция зоны в стопах.</summary>
    public float Exposure { get; init; }

    /// <summary>Авторский лог-контраст зоны.</summary>
    public float Contrast { get; init; }

    /// <summary>Авторская насыщенность зоны.</summary>
    public float Saturation { get; init; }

    /// <summary>Грейд этой зоны.</summary>
    public ColorGradeSnapshot Grade { get; init; }

    /// <summary>
    /// Вес зоны на заданной высоте: единица в ядре, ноль вне растушёвки.
    /// </summary>
    /// <remarks>
    /// Сглаживание кубическое (<c>SmoothStep</c>), а не линейное: у
    /// линейного веса производная рвётся на обеих границах, и переход
    /// читается как два толчка вместо одного плавного хода.
    /// </remarks>
    public float WeightAt(float worldY) => WeightAt(0f, worldY);

    /// <summary>
    /// Вес зоны в 2D мировой позиции: единица в ядре, ноль вне растушёвки.
    /// </summary>
    public float WeightAt(float worldX, float worldY)
    {
        if (float.IsNaN(worldY) || float.IsNaN(worldX))
        {
            return 0f;
        }

        float distanceY = Mathf.Abs(worldY - CenterY);
        float coreY = Mathf.Max(HalfHeight, 0f);
        float feather = Mathf.Max(Feather, 0f);

        float weightY;
        if (distanceY <= coreY)
        {
            weightY = 1f;
        }
        else if (feather <= 0f || distanceY >= coreY + feather)
        {
            return 0f;
        }
        else
        {
            weightY = Mathf.SmoothStep(1f, 0f, (distanceY - coreY) / feather);
        }

        if (float.IsPositiveInfinity(HalfWidth) || HalfWidth <= 0f)
        {
            return weightY;
        }

        float distanceX = Mathf.Abs(worldX - CenterX);
        float coreX = Mathf.Max(HalfWidth, 0f);
        if (distanceX <= coreX)
        {
            return weightY;
        }

        if (feather <= 0f || distanceX >= coreX + feather)
        {
            return 0f;
        }

        float weightX = Mathf.SmoothStep(1f, 0f, (distanceX - coreX) / feather);
        return weightX * weightY;
    }

    /// <summary>Приводит объявление в порядок: имя, неотрицательные размеры, грейд.</summary>
    public ColorGradeZone Sanitized() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "зона" : Name,
        CenterY = float.IsNaN(CenterY) || float.IsInfinity(CenterY) ? 0f : CenterY,
        HalfHeight = Finite(HalfHeight),
        Feather = Finite(Feather),
        CenterX = float.IsNaN(CenterX) || float.IsInfinity(CenterX) ? 0f : CenterX,
        HalfWidth = float.IsNaN(HalfWidth) ? float.PositiveInfinity : (HalfWidth < 0f ? 0f : HalfWidth),
        Exposure = FiniteClamp(
            Exposure,
            ColorGradeState.ExposureMin,
            ColorGradeState.ExposureMax,
            PostProcessLook.ColorGrading.Exposure),
        Contrast = FiniteClamp(
            Contrast,
            ColorGradeState.ContrastMin,
            ColorGradeState.ContrastMax,
            PostProcessLook.ColorGrading.Contrast),
        Saturation = FiniteClamp(
            Saturation,
            ColorGradeState.SaturationMin,
            ColorGradeState.SaturationMax,
            PostProcessLook.ColorGrading.Saturation),
        Grade = Grade.Sanitized(),
    };

    private static float Finite(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(value, 0f);

    private static float FiniteClamp(
        float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : Mathf.Clamp(value, minimum, maximum);
}
