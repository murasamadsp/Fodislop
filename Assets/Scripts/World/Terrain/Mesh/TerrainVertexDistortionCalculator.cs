#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

public readonly record struct TerrainVertexOffset(int XSteps, int YSteps, int ZSteps)
{
    public const int GridSize = 32;

    public static TerrainVertexOffset Zero => new(0, 0, 0);

    public Vector3 ToVector3()
    {
        return new Vector3(
            XSteps / (float)GridSize,
            YSteps / (float)GridSize,
            ZSteps / (float)GridSize);
    }
}

public sealed class TerrainVertexDistortionCalculator
{
    // Сила искажения: сколько шагов сетки 1/32 приходится на единицу
    // randxd/randyd. Оригинал Mines делит те же хэши на 16, мы храним
    // смещение шагами по 1/32 — поэтому двойка и есть оригинальная
    // амплитуда, а не удвоенная. Единица оставит вдвое более спокойную
    // сетку; ноль равносилен выключенному искажению.
    public const int DistortionStrengthSteps = 2;

    // Середина свободного джиттера. Хэш даёт 0..6, вычитание трёх центрирует
    // его в ноль, то есть узел уезжает в обе стороны, а не только наружу.
    private const int FreeJitterCenterSteps = 3 * DistortionStrengthSteps;

    public TerrainRingGrid<TerrainVertexOffset> GridVertexOffsets { get; } = new();

    public bool EnableDistortion { get; set; } = true;

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (GridVertexOffsets.Width != meshWidth + 1 || GridVertexOffsets.Height != meshHeight + 1)
        {
            GridVertexOffsets.EnsureSize(meshWidth + 1, meshHeight + 1);
        }
    }

    public void PrecalculateFull(TerrainCellCache cellCache, int meshWidth, int meshHeight, int worldWidth, int worldHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        int gw = meshWidth + 1;
        int gh = meshHeight + 1;
        System.Threading.Tasks.Parallel.For(0, gw, x =>
        {
            for (int y = 0; y < gh; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        });
    }

    public void PrecalculateRegion(TerrainCellCache cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight)
    {
        int gw = meshWidth + 1;
        int gh = meshHeight + 1;

        int vxMin = Mathf.Clamp(startX, 0, gw);
        int vxMax = Mathf.Clamp(startX + countX + 1, 0, gw);
        int vyMin = Mathf.Clamp(startY, 0, gh);
        int vyMax = Mathf.Clamp(startY + countY + 1, 0, gh);

        for (int x = vxMin; x < vxMax; x++)
        {
            for (int y = vyMin; y < vyMax; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        }
    }

    public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy, int worldWidth, int worldHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        // Сетка узлов на единицу больше сетки клеток: у окна w×h ровно
        // (w+1)×(h+1) углов.
        int gw = meshWidth + 1;
        int gh = meshHeight + 1;

        GridVertexOffsets.Scroll(dx, dy);

        // Кайма в один узел: узел смещается по четырём клеткам вокруг себя, и
        // у узла на старой границе клетка снаружи только что появилась.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            gw, gh, dx, dy, neighbourMargin: 1);
        CalculateBand(cellCache, bands.ColumnBand, worldWidth, worldHeight);
        CalculateBand(cellCache, bands.RowBand, worldWidth, worldHeight);
    }

    private void CalculateBand(
        TerrainCellCache cellCache,
        RectInt band,
        int worldWidth,
        int worldHeight)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            for (int y = band.yMin; y < band.yMax; y++)
            {
                CalculateVertexNode(cellCache, x, y, worldWidth, worldHeight);
            }
        }
    }

    public void CalculateVertexNode(TerrainCellCache cellCache, int x, int y, int worldWidth = int.MaxValue, int worldHeight = int.MaxValue)
    {
        if (!EnableDistortion)
        {
            GridVertexOffsets[x, y] = TerrainVertexOffset.Zero;
            return;
        }

        int cx = x + 1;
        int cy = y + 1;
        CachedCellData tl = cellCache.GetCellData(x, cy);
        CachedCellData tr = cellCache.GetCellData(cx, cy);
        CachedCellData bl = cellCache.GetCellData(x, y);
        CachedCellData br = cellCache.GetCellData(cx, y);

        int worldX = cellCache.CacheMinX + x;
        int worldY = cellCache.CacheMinY + y;

        GridVertexOffsets[x, y] = ComputeOffset(tl, tr, bl, br, worldX, worldY, worldWidth, worldHeight);
    }

    public static TerrainVertexOffset ComputeOffset(
        CachedCellData tl,
        CachedCellData tr,
        CachedCellData bl,
        CachedCellData br,
        int worldX,
        int worldY,
        int worldWidth = int.MaxValue,
        int worldHeight = int.MaxValue)
    {
        if (worldX <= 0 || worldX >= worldWidth || worldY <= 0 || worldY >= worldHeight)
        {
            return TerrainVertexOffset.Zero;
        }

        // Loose cells have their own single-cell contour in the terrain
        // shader. A shared node offset here would apply a second distortion to
        // the same cell and produce stretched lava/sand silhouettes.
        if (IsRoundableLoose(tl) || IsRoundableLoose(tr) ||
            IsRoundableLoose(bl) || IsRoundableLoose(br))
        {
            return TerrainVertexOffset.Zero;
        }

        int rx = (int)RandXd(worldX, worldY) * DistortionStrengthSteps;
        int ry = (int)RandYd(worldX, worldY) * DistortionStrengthSteps;

        // Узел внутри сплошного массива породы. Здесь нет стороны, «в которую»
        // его двигать, поэтому он ходит свободно в обе стороны — это и делает
        // кристалл цельным камнем, а не плиткой: у нас эта ветка возвращала
        // ноль, и внутренность любого массива оставалась идеальной решёткой,
        // хотя в оригинале именно она и колышется.
        //
        // Блока среди четырёх здесь заведомо нет: IsCause и IsBlock
        // несовместимы, — поэтому ветка стоит раньше проверки на блок.
        if (IsCause(tl) && IsCause(tr) && IsCause(bl) && IsCause(br))
        {
            return new TerrainVertexOffset(
                rx - FreeJitterCenterSteps,
                -(ry - FreeJitterCenterSteps),
                0);
        }

        if (IsBlock(tl) || IsBlock(tr) || IsBlock(bl) || IsBlock(br))
        {
            return TerrainVertexOffset.Zero;
        }

        if (worldY == 0 || (IsCause(tl) && IsCause(br)) || (IsCause(tr) && IsCause(bl)))
        {
            return TerrainVertexOffset.Zero;
        }

        if (IsCause(tl) && IsCause(tr))
        {
            return new TerrainVertexOffset(0, -ry, 0);
        }

        if (IsCause(tl) && IsCause(bl))
        {
            return new TerrainVertexOffset(-rx, 0, 0);
        }

        if (IsCause(tr) && IsCause(br))
        {
            return new TerrainVertexOffset(rx, 0, 0);
        }

        if (IsCause(bl) && IsCause(br))
        {
            return new TerrainVertexOffset(0, ry, 0);
        }

        if (IsCause(tl))
        {
            return new TerrainVertexOffset(-rx, -ry, 0);
        }

        if (IsCause(tr))
        {
            return new TerrainVertexOffset(rx, -ry, 0);
        }

        if (IsCause(bl))
        {
            return new TerrainVertexOffset(-rx, ry, 0);
        }

        if (IsCause(br))
        {
            return new TerrainVertexOffset(rx, ry, 0);
        }

        return TerrainVertexOffset.Zero;
    }

    public static bool IsCause(CachedCellData data)
    {
        return data.Distortion == CellDistortionType.Cause &&
            !MapCellConfigCatalog.HasFixedTerrainGeometry(data.Type);
    }

    public static bool IsBlock(CachedCellData data)
    {
        return data.Distortion == CellDistortionType.Block ||
            MapCellConfigCatalog.HasFixedTerrainGeometry(data.Type);
    }

    public static bool IsRoundableLoose(CachedCellData data)
    {
        return MapCellConfigCatalog.IsRoundableLoose(data.Type);
    }

    public static float RandXd(int x, int y)
    {
        int num = (((5 * x) + (11 * y)) * ((13 * x) + (7 * y))) % 3221;
        return (num * num) % 7;
    }

    public static float RandYd(int x, int y)
    {
        int num = (((17 * x) + (19 * y)) * ((23 * x) + (37 * y))) % 3469;
        return (num * num) % 7;
    }
}
