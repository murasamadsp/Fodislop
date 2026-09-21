#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Полосы, вошедшие в окно при сдвиге на (dx, dy).
/// </summary>
///
/// Кольцевая сетка переносит перекрытие даром, пересчитать надо только то, что
/// вошло с краёв. Это ровно два прямоугольника: полоса по x во всю высоту и
/// полоса по y на той ширине, которую полоса по x не накрыла. Угол принадлежит
/// первой из них и второй раз не считается.
///
/// Тип один на все инкрементальные пути террейна — кэш клеток, предрасчёт
/// масок, узлы искажения, заливка фона и сборка текселей. Раньше каждый считал
/// эти границы сам, четырьмя не совпадающими формулами, и сверить их между
/// собой было нечем.
///
/// <paramref name="neighbourMargin"/> — сколько клеток вглубь окна захватывает
/// полоса сверх вошедших. Ноль у тех, кто смотрит только на свою клетку;
/// единица у тех, кто читает соседей: у клетки на старой границе теперь
/// появился сосед снаружи, и её значение больше не верно.
public readonly record struct TerrainScrollBands(RectInt ColumnBand, RectInt RowBand)
{
    public static TerrainScrollBands Resolve(
        int width,
        int height,
        int dx,
        int dy,
        int neighbourMargin = 0)
    {
        ResolveAxis(width, dx, neighbourMargin, out int columnStart, out int columnLength);
        ResolveAxis(height, dy, neighbourMargin, out int rowStart, out int rowLength);

        var columnBand = columnLength > 0
            ? new RectInt(columnStart, 0, columnLength, height)
            : default;

        // Оставшаяся ширина: полоса по y берёт только её. Иначе угол попал бы
        // в обработку дважды — а у заливки фона это ещё и вторая запись в
        // волну, то есть другой результат, а не просто лишняя работа.
        int remainingStart = columnLength == 0 ? 0 : dx > 0 ? 0 : columnLength;
        int remainingEnd = columnLength == 0 ? width : dx > 0 ? columnStart : width;
        var rowBand = rowLength > 0 && remainingEnd > remainingStart
            ? new RectInt(remainingStart, rowStart, remainingEnd - remainingStart, rowLength)
            : default;

        return new TerrainScrollBands(columnBand, rowBand);
    }

    private static void ResolveAxis(
        int size,
        int delta,
        int neighbourMargin,
        out int start,
        out int length)
    {
        if (delta > 0)
        {
            start = Mathf.Max(0, size - delta - neighbourMargin);
            length = size - start;
        }
        else if (delta < 0)
        {
            start = 0;
            length = Mathf.Min(size, -delta + neighbourMargin);
        }
        else
        {
            start = 0;
            length = 0;
        }
    }
}
