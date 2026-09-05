#nullable enable

using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;
/// <summary>Примары, в которых экран ждёт кадр.</summary>
/// <remarks>
/// Значения совпадают с <c>FODINAE_GAMUT_*</c> в <c>ColorGrading.hlsl</c>:
/// это одно и то же число по обе стороны границы CPU/GPU.
/// </remarks>
public enum DisplayGamutKind
{
    /// <summary>Rec.709 / sRGB — примары рендера, пересчёт не нужен.</summary>
    Rec709 = 0,

    /// <summary>Display P3 — широкий гамут macOS и iOS.</summary>
    DisplayP3 = 1,

    /// <summary>Rec.2020 — примары HDR-выхода.</summary>
    Rec2020 = 2,
}

/// <summary>
/// Определяет выходной гамут по тому, что реально выбрала графика.
/// </summary>
/// <remarks>
/// СПРАШИВАЕМ ГРАФИКУ, А НЕ ДИСПЛЕЙ. <c>Graphics.activeColorGamut</c> — это
/// гамут, в котором построена цепочка кадров ПРЯМО СЕЙЧАС, то есть тот, в
/// котором система истолкует наши числа. Способность дисплея тут ни при чём:
/// на макбуке с DCI-P3 цепочка останется sRGB, пока Display P3 не объявлен в
/// <c>m_ColorGamuts</c> проекта, и пересчёт в P3 при sRGB-цепочке дал бы
/// ровно ту ошибку, которую он призван убрать, только в другую сторону.
///
/// Отсюда же и осторожный разбор: неизвестное значение считается Rec.709.
/// Не пересчитать — значит показать кадр как раньше; пересчитать не туда —
/// значит испортить все цвета разом.
/// </remarks>
public static class DisplayGamut
{
    public static DisplayGamutKind Current => FromColorGamut(Graphics.activeColorGamut);

    public static DisplayGamutKind FromColorGamut(ColorGamut gamut) => gamut switch
    {
        ColorGamut.DisplayP3 or ColorGamut.P3D65G22 => DisplayGamutKind.DisplayP3,
        ColorGamut.Rec2020 or ColorGamut.HDR10 or ColorGamut.DolbyHDR => DisplayGamutKind.Rec2020,
        _ => DisplayGamutKind.Rec709,
    };
}
