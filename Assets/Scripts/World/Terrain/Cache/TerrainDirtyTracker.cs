#nullable enable

using System;
using Kern.Core;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Что в мире изменилось с прошлого кадра и стоит ли это заплатки.
/// </summary>
///
/// Изменение приходит в серверных координатах, а окно террейна живёт в
/// координатах Unity: ось Y перевёрнута, поэтому прямоугольник переводится
/// целиком, а не по одной точке. Изменение вне окна не копится — оно приедет
/// вместе с окном, когда камера туда доедет.
///
/// Копить заплатки бесконечно нельзя: с какого-то числа прямоугольников один
/// проход по окну дешевле, и тогда трекер просит полную сборку вместо набора
/// заплаток (см. <see cref="TerrainRebuildCostModel"/>).
public sealed class TerrainDirtyTracker
{
    private readonly DirtyRectSet _rects = new();

    public DirtyRectSet Rects => _rects;

    public bool IsEmpty => _rects.IsEmpty;

    public void Clear() => _rects.Clear();

    /// <summary>
    /// Учесть изменение мира. Возвращает прямоугольник в координатах Unity,
    /// если он задевает окно, иначе <c>null</c>: освещению незачем сбрасывать
    /// регион, которого нет на экране.
    /// </summary>
    public RectInt? Add(
        int serverX,
        int serverY,
        int width,
        int height,
        Vector2Int windowOrigin,
        int meshWidth,
        int meshHeight,
        int worldHeight)
    {
        int lastServerY = serverY + Mathf.Max(0, height - 1);
        int firstUnityY = Mathf.FloorToInt(CoordinateUtils.ServerToUnityY(serverY, worldHeight));
        int lastUnityY = Mathf.FloorToInt(CoordinateUtils.ServerToUnityY(lastServerY, worldHeight));
        int minimumUnityY = Mathf.Min(firstUnityY, lastUnityY);
        int maximumUnityY = Mathf.Max(firstUnityY, lastUnityY);
        bool affectsCachedTerrain =
            serverX + width - 1 >= windowOrigin.x - 1 &&
            serverX <= windowOrigin.x + meshWidth &&
            maximumUnityY >= windowOrigin.y - 1 &&
            minimumUnityY <= windowOrigin.y + meshHeight;
        if (!affectsCachedTerrain)
        {
            return null;
        }

        RectInt changedRegion = new(
            serverX,
            minimumUnityY,
            width,
            maximumUnityY + 1 - minimumUnityY);
        _rects.Add(
            changedRegion,
            new RectInt(windowOrigin.x, windowOrigin.y, meshWidth, meshHeight));
        return changedRegion;
    }

    /// <summary>
    /// Заплатки перестали окупаться: очистить их и сообщить, что окно надо
    /// собрать целиком.
    /// </summary>
    public bool CoalesceIntoFullRebuild(Vector2Int windowOrigin, int meshWidth, int meshHeight)
    {
        if (!TerrainRebuildCostModel.PrefersFullRebuild(
            _rects,
            new RectInt(windowOrigin.x, windowOrigin.y, meshWidth, meshHeight),
            meshWidth,
            meshHeight))
        {
            return false;
        }

        _rects.Clear();
        return true;
    }
}
