#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Fodinae.World.Terrain;

/// <summary>
/// Сдвигает плоские буферы сетки террейна на целое число клеток.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Кэш клеток, предрасчёт и заливка фона при переходе через границу
/// региона едут сдвигом: <see cref="TerrainCacheArrayScroller"/> и
/// <c>BackgroundFloodFill.ComputeScrolled</c> платят по кайме, а не по
/// площади. Сборка меша ехала полной пересборкой — те же данные считались
/// заново для всех клеток окна, из которых сдвиг открыл единицы процентов.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ СКРОЛЛЕР. <see cref="TerrainCacheArrayScroller"/> работает
/// с <c>T[,]</c> поэлементно. Буферы сетки плоские и по-столбцовые
/// (<c>x * height + y</c>), а вершин на клетку восемь, поэтому столбец здесь —
/// непрерывный отрезок памяти, и сдвиг стоит одного <see cref="Array.Copy"/>
/// на столбец вместо цикла по элементам.
///
/// Соглашение о знаке — то же, что у кэша: положительное <c>dx</c> означает,
/// что окно уехало вправо, и приёмник берёт данные из <c>x + dx</c>.
/// </remarks>
internal static class TerrainMeshScroller
{
    /// <summary>
    /// Переносит уцелевшую часть буфера на новое место.
    /// </summary>
    /// <param name="buffer">Плоский буфер, разложенный по-столбцовому.</param>
    /// <param name="width">Ширина сетки в клетках.</param>
    /// <param name="height">Высота сетки в клетках.</param>
    /// <param name="elementsPerCell">Сколько элементов буфера приходится на клетку.</param>
    /// <param name="dx">Сдвиг окна по x в клетках.</param>
    /// <param name="dy">Сдвиг окна по y в клетках.</param>
    public static void Scroll<T>(
        T[] buffer,
        int width,
        int height,
        int elementsPerCell,
        int dx,
        int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        int keptWidth = width - Math.Abs(dx);
        int keptHeight = height - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int sourceY = dy > 0 ? dy : 0;
        int targetY = dy > 0 ? 0 : -dy;
        int runLength = keptHeight * elementsPerCell;

        // Столбцы перекрываются, и порядок обхода решает, не затрёт ли
        // приёмник ещё не прочитанный источник: при сдвиге вправо приёмник
        // левее источника, поэтому идём слева направо, и наоборот.
        // Внутри столбца перекрытие безопасно само по себе: Array.Copy для
        // одного массива ведёт себя как memmove.
        if (dx >= 0)
        {
            int firstTargetX = 0;
            for (int x = firstTargetX; x < keptWidth; x++)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
        else
        {
            int firstTargetX = -dx;
            for (int x = firstTargetX + keptWidth - 1; x >= firstTargetX; x--)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
    }

    /// <summary>
    /// Снимает с перенесённых вершин локальное смещение прежнего места.
    /// </summary>
    /// <remarks>
    /// Из всех полей вершины на локальные координаты завязана одна позиция:
    /// она равна <c>(x, y) * cellSize</c> плюс смещение искажения, а смещение
    /// живёт при клетке мира и едет вместе с данными. Клетка, переехавшая из
    /// <c>x + dx</c> в <c>x</c>, обязана потерять ровно <c>dx * cellSize</c>.
    /// Остальные поля — мировые координаты, атлас, маски — верны на новом
    /// месте без правки, поэтому правка стоит одного вычитания на вершину.
    /// </remarks>
    public static void ShiftPositions(
        TerrainVertex[] vertices,
        int meshWidth,
        int meshHeight,
        int verticesPerCell,
        float cellSize,
        int dx,
        int dy)
    {
        int keptWidth = meshWidth - Math.Abs(dx);
        int keptHeight = meshHeight - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0 || (dx == 0 && dy == 0))
        {
            return;
        }

        int firstX = dx > 0 ? 0 : -dx;
        int firstY = dy > 0 ? 0 : -dy;
        float shiftX = dx * cellSize;
        float shiftY = dy * cellSize;

        Parallel.For(firstX, firstX + keptWidth, x =>
        {
            int start = ((x * meshHeight) + firstY) * verticesPerCell;
            int end = start + (keptHeight * verticesPerCell);
            for (int i = start; i < end; i++)
            {
                ref TerrainVertex vertex = ref vertices[i];
                vertex.Position.x -= shiftX;
                vertex.Position.y -= shiftY;
            }
        });
    }

    /// <summary>
    /// Возвращает кайму, открывшуюся сдвигом, расширенную на клетку внутрь.
    /// </summary>
    /// <remarks>
    /// Расширение не запас на всякий случай: маски соседства у крайней
    /// внутренней клетки считаются по клетке снаружи, и предрасчёт
    /// (<c>TerrainCellMaskCalculator.PrecalculateIncremental</c>) берёт ровно
    /// такую же кайму. Разойтись с ним значит оставить шов, собранный по
    /// маскам прошлого положения окна.
    /// </remarks>
    public static void GetBandExtents(int size, int delta, out int start, out int length)
    {
        if (delta > 0)
        {
            start = Mathf.Max(0, size - delta - 1);
            length = size - start;
        }
        else if (delta < 0)
        {
            start = 0;
            length = Mathf.Min(size, -delta + 1);
        }
        else
        {
            start = 0;
            length = 0;
        }
    }

    private static void CopyColumn<T>(
        T[] buffer,
        int targetX,
        int sourceX,
        int height,
        int elementsPerCell,
        int sourceY,
        int targetY,
        int runLength)
    {
        int sourceOffset = ((sourceX * height) + sourceY) * elementsPerCell;
        int targetOffset = ((targetX * height) + targetY) * elementsPerCell;
        Array.Copy(buffer, sourceOffset, buffer, targetOffset, runLength);
    }
}
