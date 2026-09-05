#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>
/// Набор зон грейда и его разрешение в один снимок.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ЭТО ВООБЩЕ НУЖНО. Один грейд на всю игру означает, что поверхность
/// и глубина красятся одинаково, — а это ровно то различие, ради которого
/// в кино и красят посценно. Игре оно нужнее вторичных коррекций: разница
/// между «наверху» и «внизу» видна каждому игроку постоянно, а разница в
/// оттенке отдельного объекта — почти никому и никогда.
///
/// БАЗА ВСЕГДА ЕСТЬ. Разрешение начинается с <see cref="ColorGradeSnapshot.FromLook"/>
/// и накладывает зоны поверх по весам. Поэтому дыра между зонами — не
/// «нет грейда», а авторский вид по умолчанию: кадр не может остаться без
/// кривой из-за того, что автор не покрыл зонами весь мир.
///
/// ПОРЯДОК ЗНАЧИМ. Зоны накладываются в объявленном порядке, и при
/// перекрытии верхняя перевешивает по своему весу. Это то же правило, что
/// у слоёв грейда, и второго правила помнить не надо.
/// </remarks>
public sealed class ColorGradeZones
{
    public readonly record struct Resolution(
        ColorGradeSnapshot Grade,
        float Exposure,
        float Contrast,
        float Saturation)
    {
        public static Resolution FromLook() => new(
            ColorGradeSnapshot.FromLook(),
            PostProcessLook.ColorGrading.Exposure,
            PostProcessLook.ColorGrading.Contrast,
            PostProcessLook.ColorGrading.Saturation);

        public Resolution BlendTo(Resolution other, float weight)
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

            return new Resolution(
                Grade.BlendTo(other.Grade, t),
                Mathf.Lerp(Exposure, other.Exposure, t),
                Mathf.Lerp(Contrast, other.Contrast, t),
                Mathf.Lerp(Saturation, other.Saturation, t));
        }
    }

    private readonly List<ColorGradeZone> _zones = new();

    public IReadOnlyList<ColorGradeZone> Zones => _zones;

    public int Count => _zones.Count;

    /// <summary>Считать ли зоны вообще. Выключено — работает единый грейд.</summary>
    public bool Enabled { get; set; }

    public void Clear() => _zones.Clear();

    public void Add(ColorGradeZone zone) => _zones.Add(zone.Sanitized());

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _zones.Count)
        {
            _zones.RemoveAt(index);
        }
    }

    public void Replace(int index, ColorGradeZone zone)
    {
        if (index >= 0 && index < _zones.Count)
        {
            _zones[index] = zone.Sanitized();
        }
    }

    /// <summary>
    /// Собирает грейд для заданной высоты поверх переданной базы.
    /// </summary>
    public Resolution Resolve(Resolution baseGrade, float worldY) =>
        Resolve(baseGrade, 0f, worldY);

    /// <summary>
    /// Собирает грейд для 2D координат в мире поверх переданной базы.
    /// </summary>
    public Resolution Resolve(Resolution baseGrade, float worldX, float worldY)
    {
        if (!Enabled || _zones.Count == 0)
        {
            return baseGrade;
        }

        Resolution result = baseGrade;
        for (int index = 0; index < _zones.Count; index++)
        {
            ColorGradeZone zone = _zones[index];
            float weight = zone.WeightAt(worldX, worldY);
            if (weight > 0f)
            {
                result = result.BlendTo(
                    new Resolution(
                        zone.Grade,
                        zone.Exposure,
                        zone.Contrast,
                        zone.Saturation),
                    weight);
            }
        }

        return result;
    }

    /// <summary>Имя зоны с наибольшим весом — для показа в инструменте автора.</summary>
    public string DescribeAt(float worldY) => DescribeAt(0f, worldY);

    /// <summary>Имя зоны с наибольшим весом в позиции (X, Y).</summary>
    public string DescribeAt(float worldX, float worldY)
    {
        string name = "база";
        float best = 0f;
        for (int index = 0; index < _zones.Count; index++)
        {
            float weight = _zones[index].WeightAt(worldX, worldY);
            if (weight > best)
            {
                best = weight;
                name = _zones[index].Name;
            }
        }

        return best > 0f ? $"{name} ({best:P0})" : name;
    }
}
