#nullable enable

using UnityEngine;

namespace Fodinae.World.Terrain;

/// <summary>
/// Coordinates precalculation of vertex distortion offsets and cell topology masks across the terrain grid.
/// </summary>
public class TerrainPrecalculator
{
    private readonly TerrainVertexDistortionCalculator _distortion = new();
    private readonly TerrainCellMaskCalculator _cellMask = new();

    public Vector3[,] GridVertexOffsets => _distortion.GridVertexOffsets;

    public int[,] CellTilingDescriptors => _cellMask.CellTilingDescriptors;

    public int[,] CellCornerVariants => _cellMask.CellCornerVariants;

    public byte[,] CellReliefMasks => _cellMask.CellReliefMasks;

    public byte[,] CellSolidBoundaryMasks => _cellMask.CellSolidBoundaryMasks;

    public bool EnableDistortion
    {
        get => _distortion.EnableDistortion;
        set => _distortion.EnableDistortion = value;
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        _distortion.EnsureCapacity(meshWidth, meshHeight);
        _cellMask.EnsureCapacity(meshWidth, meshHeight);
    }

    public void PrecalculateFull(TerrainCellCache cellCache, int meshWidth, int meshHeight, int worldWidth, int worldHeight)
    {
        _distortion.PrecalculateFull(cellCache, meshWidth, meshHeight, worldWidth, worldHeight);
        _cellMask.PrecalculateFull(cellCache, meshWidth, meshHeight);
    }

    public void PrecalculateRegion(TerrainCellCache cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY, int worldWidth, int worldHeight)
    {
        _distortion.PrecalculateRegion(cellCache, meshWidth, meshHeight, startX, startY, countX, countY, worldWidth, worldHeight);
        _cellMask.PrecalculateRegion(cellCache, meshWidth, meshHeight, startX, startY, countX, countY);
    }

    public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy, int worldWidth, int worldHeight)
    {
        _distortion.PrecalculateIncremental(cellCache, meshWidth, meshHeight, dx, dy, worldWidth, worldHeight);
        _cellMask.PrecalculateIncremental(cellCache, meshWidth, meshHeight, dx, dy);
    }
}
