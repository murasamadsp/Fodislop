#nullable enable

using System;
using UnityEngine;

namespace Fodinae.World.Terrain;

// Изменённые тексели текстур клетки с прошлой выгрузки.
//
// Несколько прямоугольников, а не один общий: при диагональном шаге полоса
// по x занимает всю высоту, по y — всю ширину, и их объединение было бы всей
// текстурой на каждом таком шаге. Прямоугольники приходят в кольцевых
// координатах клеток и режутся на шве кольца. Отдельный тип без Texture2D —
// чтобы учёт мерился и проверялся вне Unity.
public sealed class TerrainDirtyRegion
{
    public const int MaxRects = 4;

    private readonly RectInt[] _rects = new RectInt[MaxRects];
    private int _cellWidth;
    private int _cellHeight;

    public int Count { get; private set; }

    public bool IsAll { get; private set; }

    public bool IsEmpty => !IsAll && Count == 0;

    public RectInt this[int index] => _rects[index];

    // Площадь в текселях (по две строки на клетку).
    public long Area
    {
        get
        {
            long area = 0;
            for (int i = 0; i < Count; i++)
            {
                area += (long)_rects[i].width * _rects[i].height;
            }

            return area;
        }
    }

    public void Reset(int cellWidth, int cellHeight)
    {
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
        MarkAll();
    }

    public void MarkAll()
    {
        IsAll = true;
        Count = 0;
    }

    public void Clear()
    {
        IsAll = false;
        Count = 0;
    }

    public void MarkCells(int ringX, int ringY, int width, int height)
    {
        if (IsAll || width <= 0 || height <= 0 || _cellWidth <= 0 || _cellHeight <= 0)
        {
            return;
        }

        width = Math.Min(width, _cellWidth);
        height = Math.Min(height, _cellHeight);
        int rightPart = ringX + width - _cellWidth;
        int topPart = ringY + height - _cellHeight;
        int leftWidth = width - Math.Max(0, rightPart);
        int bottomHeight = height - Math.Max(0, topPart);

        AddCellRect(ringX, ringY, leftWidth, bottomHeight);
        if (rightPart > 0)
        {
            AddCellRect(0, ringY, rightPart, bottomHeight);
        }

        if (topPart > 0)
        {
            AddCellRect(ringX, 0, leftWidth, topPart);
            if (rightPart > 0)
            {
                AddCellRect(0, 0, rightPart, topPart);
            }
        }
    }

    private void AddCellRect(int x, int y, int width, int height)
    {
        var rect = new RectInt(
            x,
            y * TerrainCellDataPacker.LayersPerCell,
            width,
            height * TerrainCellDataPacker.LayersPerCell);
        // Сливается только то, что не раздувает площадь: перекрытие или стык
        // по целой стороне. Раньше сливалось всё соприкасающееся, и полосы x
        // и y диагонального шага, касаясь в углу, давали прямоугольник во всю
        // текстуру — бенчмарк показал 100%.
        for (int i = 0; i < Count; i++)
        {
            if (Waste(_rects[i], rect) <= 0)
            {
                _rects[i] = Union(_rects[i], rect);
                return;
            }
        }

        if (Count < MaxRects)
        {
            _rects[Count++] = rect;
            return;
        }

        // Сверх лимита склеивается пара с наименьшим лишним приростом площади,
        // а не все сразу: рассыпанные заплатки остаются маленькими.
        int bestA = -1;
        int bestB = -1;
        long bestWaste = long.MaxValue;
        for (int a = 0; a < Count; a++)
        {
            long waste = Waste(_rects[a], rect);
            if (waste < bestWaste)
            {
                (bestWaste, bestA, bestB) = (waste, a, -1);
            }

            for (int b = a + 1; b < Count; b++)
            {
                waste = Waste(_rects[a], _rects[b]);
                if (waste < bestWaste)
                {
                    (bestWaste, bestA, bestB) = (waste, a, b);
                }
            }
        }

        if (bestB < 0)
        {
            _rects[bestA] = Union(_rects[bestA], rect);
            return;
        }

        _rects[bestA] = Union(_rects[bestA], _rects[bestB]);
        _rects[bestB] = rect;
    }

    private static long RectArea(RectInt rect) => (long)rect.width * rect.height;

    // Площадь объединения сверх суммы площадей; для перекрытия может быть
    // отрицательной — тогда слияние только выгоднее.
    private static long Waste(RectInt a, RectInt b) => RectArea(Union(a, b)) - RectArea(a) - RectArea(b);

    private static RectInt Union(RectInt a, RectInt b)
    {
        int xMin = Math.Min(a.xMin, b.xMin);
        int yMin = Math.Min(a.yMin, b.yMin);
        return new RectInt(xMin, yMin, Math.Max(a.xMax, b.xMax) - xMin, Math.Max(a.yMax, b.yMax) - yMin);
    }
}
