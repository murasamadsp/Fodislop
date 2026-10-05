#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

// Правила соседства клетки: автотайл, углы стены пака, рельеф и твёрдые
// соседи. Шейдер выводит то же из буфера клеток (TerrainCellData.hlsl);
// здесь — CPU-сторона для вершин накладки дверей и эталона тестов.
public static class TerrainCellMaskCalculator
{
    public static int CalculateTilingDescriptor(
        CachedCellData data,
        CachedCellData left,
        CachedCellData bottomLeft,
        CachedCellData bottom,
        CachedCellData bottomRight,
        CachedCellData right,
        CachedCellData topRight,
        CachedCellData top,
        CachedCellData topLeft)
    {
        if (!data.HasTileGroup)
        {
            return 0;
        }

        byte m = 0;
        if (left.HasTileGroup && left.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 0;
        }

        if (bottomLeft.HasTileGroup && bottomLeft.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 1;
        }

        if (bottom.HasTileGroup && bottom.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 2;
        }

        if (bottomRight.HasTileGroup && bottomRight.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 3;
        }

        if (right.HasTileGroup && right.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 4;
        }

        if (topRight.HasTileGroup && topRight.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 5;
        }

        if (top.HasTileGroup && top.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 6;
        }

        if (topLeft.HasTileGroup && topLeft.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 7;
        }

        return TileBitmaskConverter.GetDescriptor(m);
    }

    public static int CalculateCornerSideMask(
        CachedCellData data,
        CachedCellData left,
        CachedCellData right,
        CachedCellData top,
        CachedCellData bottom)
    {
        int cornerSideMask = 0;
        if (MapCellConfigCatalog.GetVisualProperties(data.Type).IsBuildingWall)
        {
            if (MapCellConfigCatalog.GetVisualProperties(left.Type).IsBuildingCorner)
            {
                cornerSideMask |= 1;
            }

            if (MapCellConfigCatalog.GetVisualProperties(right.Type).IsBuildingCorner)
            {
                cornerSideMask |= 2;
            }

            if (MapCellConfigCatalog.GetVisualProperties(top.Type).IsBuildingCorner)
            {
                cornerSideMask |= 4;
            }

            if (MapCellConfigCatalog.GetVisualProperties(bottom.Type).IsBuildingCorner)
            {
                cornerSideMask |= 8;
            }
        }

        return cornerSideMask;
    }

    // Рельефная маска: бит стоит там, где сосед принадлежит той же рельефной
    // поверхности. Обычно это ненулевая серверная группа. Непрерывные листы
    // также объединяются по семейству; зелёный и синий кристаллы с пустоскалом
    // образуют собственное исключительное семейство и не сливаются с ними.
    //
    // Сравнение именно на равенство, а не «сосед не ниже». Кайма рисуется по
    // сторонам, где сосед чужой, и порядковое сравнение делало её
    // односторонней: кристалл (группа 3) рядом с неразрушимой породой
    // (группа 4) считал соседа своим и сливался с ним, а порода рядом с
    // кристаллом — чужим и обводилась. Шов получался у одной клетки из двух.
    // В оригинале сравнение равенством, и обе стороны обводят друг друга.
    public static byte CalculateReliefMask(
        CachedCellData data,
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right)
    {
        if (data.ReliefGroup == 0)
        {
            return 0;
        }

        byte rm = 0;
        if (SameReliefSurface(data, top))
        {
            rm |= 1;
        }

        if (SameReliefSurface(data, left))
        {
            rm |= 2;
        }

        if (SameReliefSurface(data, bottom))
        {
            rm |= 4;
        }

        if (SameReliefSurface(data, right))
        {
            rm |= 8;
        }

        return rm;
    }

    // Одна масса — одна группа каймы; группа 0 каймы не имеет.
    private static bool SameReliefSurface(in CachedCellData first, in CachedCellData second) =>
        second.ReliefGroup != 0 && first.ReliefGroup == second.ReliefGroup;

    internal static void CalculateReliefMasks(
        in CachedCellData data,
        in CachedCellData top,
        in CachedCellData left,
        in CachedCellData bottom,
        in CachedCellData right,
        in CachedCellData topLeft,
        in CachedCellData topRight,
        in CachedCellData bottomLeft,
        in CachedCellData bottomRight,
        out byte reliefMask,
        out byte reliefCornerMask)
    {
        reliefMask = 0;
        reliefCornerMask = 0;
        if (data.ReliefGroup == 0)
        {
            return;
        }

        bool topSame = SameReliefSurface(data, top);
        bool leftSame = SameReliefSurface(data, left);
        bool bottomSame = SameReliefSurface(data, bottom);
        bool rightSame = SameReliefSurface(data, right);

        if (topSame)
        {
            reliefMask |= 1;
        }

        if (leftSame)
        {
            reliefMask |= 2;
        }

        if (bottomSame)
        {
            reliefMask |= 4;
        }

        if (rightSame)
        {
            reliefMask |= 8;
        }

        reliefCornerMask = CalculateReliefCornerMask(
            data,
            top,
            left,
            bottom,
            right,
            topLeft,
            topRight,
            bottomLeft,
            bottomRight);
    }

    // Вогнутый угол силуэта: обе кардинальные клетки принадлежат поверхности,
    // диагональная — нет. Одной маски сторон для такого шаблона недостаточно.
    public static byte CalculateReliefCornerMask(
        CachedCellData data,
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right,
        CachedCellData topLeft,
        CachedCellData topRight,
        CachedCellData bottomLeft,
        CachedCellData bottomRight)
    {
        if (data.ReliefGroup == 0)
        {
            return 0;
        }

        byte cornerMask = 0;
        if (SameReliefSurface(data, bottom) && SameReliefSurface(data, left) &&
            !SameReliefSurface(data, bottomLeft))
        {
            cornerMask |= 1 << 0;
        }

        if (SameReliefSurface(data, bottom) && SameReliefSurface(data, right) &&
            !SameReliefSurface(data, bottomRight))
        {
            cornerMask |= 1 << 1;
        }

        if (SameReliefSurface(data, top) && SameReliefSurface(data, right) &&
            !SameReliefSurface(data, topRight))
        {
            cornerMask |= 1 << 2;
        }

        if (SameReliefSurface(data, top) && SameReliefSurface(data, left) &&
            !SameReliefSurface(data, topLeft))
        {
            cornerMask |= 1 << 3;
        }

        return cornerMask;
    }

    public static byte CalculateSolidBoundaryMask(
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right)
    {
        byte solidMask = 0;
        if (CastsShadow(top))
        {
            solidMask |= 1;
        }

        if (CastsShadow(left))
        {
            solidMask |= 2;
        }

        if (CastsShadow(bottom))
        {
            solidMask |= 4;
        }

        if (CastsShadow(right))
        {
            solidMask |= 8;
        }

        return solidMask;
    }

    // Тень отбрасывает загруженная клетка, через которую нельзя пройти.
    private static bool CastsShadow(in CachedCellData cell) =>
        cell.State == TerrainCellState.Loaded &&
        cell.Type != CellType.Unloaded &&
        (cell.Properties & CellConfigProperties.Passable) == 0;
}
