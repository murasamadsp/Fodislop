#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Что дешевле: заплатать изменённые клетки или пересобрать окно целиком.
/// </summary>
///
/// Заплатка платит по площади своих прямоугольников, полная сборка — по
/// площади окна. Копка в одной клетке дешевле сборки всегда; сотня
/// разбросанных прямоугольников — уже нет, и тогда честнее один проход.
///
/// Работа считается в клетках, а не в миллисекундах: и заплатка, и сборка
/// делают над клеткой одни и те же шаги, поэтому отношение площадей — это и
/// есть отношение стоимостей, без калибровки под машину.
///
/// ЧЕГО ЗДЕСЬ НЕТ, И ПОЧЕМУ ЭТО ВАЖНО. Обе оценки описывают только обход
/// клеток. Полная пересборка вдобавок заново разрешает метаданные всего окна,
/// строит заново пространственный индекс типов и метит текстуры целиком —
/// Apply() выгружает тогда все девять каналов, а не прямоугольники заплатки.
/// Ничего из этого в числах ниже нет.
///
/// Поэтому порог нельзя двигать «в пользу полной пересборки» из рассуждений о
/// распараллеливании: попытка так сделать (делить параллельные стадии на число
/// ядер) уронила fps в шесть раз. Стадии обхода действительно параллельны у
/// полного пути и последовательны у заплатки, но выигрыш от этого меньше, чем
/// незаложенная в модель цена полной выгрузки, а при непрерывном стриминге
/// выбор повторяется каждый кадр. Двигать порог можно только вместе с
/// измеренной ценой выгрузки и переиндексации.
///
/// Переиндексация с тех пор измерена и перестала быть тяжёлой: проход по
/// всему окну в CellTypeSpatialIndex стоит ~0.08 мс вместо ~34 мс, с тех пор
/// как обратная сторона индекса легла плотным массивом по кольцевому адресу.
/// Незаложенной остаётся цена полной выгрузки девяти каналов.
public static class TerrainRebuildCostModel
{
    /// <summary>Единицы работы полной пересборки окна meshWidth × meshHeight.</summary>
    ///
    /// Слагаемые по порядку: кэш клеток с каймой, узлы сетки искажения,
    /// предрасчёт масок, заливка фона, сборка текселей.
    public static long EstimateFullWindow(int meshWidth, int meshHeight)
    {
        long cells = (long)meshWidth * meshHeight;
        long cacheCells = (long)(meshWidth + 2) * (meshHeight + 2);
        long gridNodes = (long)(meshWidth + 1) * (meshHeight + 1);
        return cacheCells + gridNodes + cells + cells + cells;
    }

    /// <summary>Стоимость заплатки по набору изменённых прямоугольников.</summary>
    ///
    /// Каждый прямоугольник расширяется на клетку во все стороны: соседство
    /// изменившейся клетки тоже надо пересчитать.
    public static long EstimateDirtyPatch(
        DirtyRectSet dirtyRects,
        RectInt terrainBounds,
        int meshWidth,
        int meshHeight)
    {
        long work = 0;
        for (int index = 0; index < dirtyRects.Count; index++)
        {
            RectInt dirty = dirtyRects[index];
            int startX = Mathf.Clamp(dirty.xMin - terrainBounds.xMin - 1, 0, meshWidth);
            int endX = Mathf.Clamp(dirty.xMax - terrainBounds.xMin + 1, 0, meshWidth);
            int startY = Mathf.Clamp(dirty.yMin - terrainBounds.yMin - 1, 0, meshHeight);
            int endY = Mathf.Clamp(dirty.yMax - terrainBounds.yMin + 1, 0, meshHeight);
            int width = Mathf.Max(0, endX - startX);
            int height = Mathf.Max(0, endY - startY);
            if (width == 0 || height == 0)
            {
                continue;
            }

            long cells = (long)width * height;
            work += cells + ((long)(width + 1) * (height + 1)) + cells + cells;
        }

        return work;
    }

    /// <summary>Заплатка уже не окупается — дешевле собрать окно заново.</summary>
    public static bool PrefersFullRebuild(
        DirtyRectSet dirtyRects,
        RectInt terrainBounds,
        int meshWidth,
        int meshHeight)
    {
        if (dirtyRects.IsEmpty || meshWidth <= 0 || meshHeight <= 0)
        {
            return false;
        }

        return EstimateDirtyPatch(dirtyRects, terrainBounds, meshWidth, meshHeight) >=
            EstimateFullWindow(meshWidth, meshHeight);
    }
}
