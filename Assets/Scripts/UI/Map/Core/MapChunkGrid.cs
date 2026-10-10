#nullable enable

using System;

namespace Kern.UI;

/// <summary>
/// Visible GPU-chunk grid for one map render. The viewport renderer and the mip
/// budget both derive their bounds from this single computation, so the decision
/// to use the mip path can never disagree with the grid that would be packed.
/// </summary>
internal readonly struct MapChunkGrid
{
    private MapChunkGrid(
        int minChunkX,
        int minChunkY,
        int width,
        int height,
        float startWorldX,
        float startWorldY)
    {
        MinChunkX = minChunkX;
        MinChunkY = minChunkY;
        Width = width;
        Height = height;
        StartWorldX = startWorldX;
        StartWorldY = startWorldY;
    }

    public int MinChunkX { get; }

    public int MinChunkY { get; }

    public int Width { get; }

    public int Height { get; }

    public float StartWorldX { get; }

    public float StartWorldY { get; }

    public int Area => Width * Height;

    public bool Matches(MapChunkGrid other) =>
        MinChunkX == other.MinChunkX &&
        MinChunkY == other.MinChunkY &&
        Width == other.Width &&
        Height == other.Height;

    public static MapChunkGrid Compute(
        int worldWidth,
        int worldHeight,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || texWidth <= 0 || texHeight <= 0 ||
            cellsPerPixel <= 0f || float.IsNaN(cellsPerPixel) || float.IsInfinity(cellsPerPixel))
        {
            return default;
        }

        int gpuChunkSize = MapViewportChunkBudget.GpuChunkSize;
        float startWorldX = viewCenterX + (0.5f - (texWidth * 0.5f)) * cellsPerPixel;
        float startWorldY = viewCenterY + ((texHeight * 0.5f) - 0.5f) * cellsPerPixel;
        float endWorldX = startWorldX + ((texWidth - 1) * cellsPerPixel);
        float otherWorldY = startWorldY - ((texHeight - 1) * cellsPerPixel);

        int maxChunkCoordX = MaxChunkCoord(worldWidth, gpuChunkSize);
        int maxChunkCoordY = MaxChunkCoord(worldHeight, gpuChunkSize);

        int minChunkX = ClampChunk(startWorldX, gpuChunkSize, maxChunkCoordX);
        int maxChunkX = ClampChunk(endWorldX, gpuChunkSize, maxChunkCoordX);
        int minChunkY = ClampChunk(Math.Min(startWorldY, otherWorldY), gpuChunkSize, maxChunkCoordY);
        int maxChunkY = ClampChunk(Math.Max(startWorldY, otherWorldY), gpuChunkSize, maxChunkCoordY);

        return new MapChunkGrid(
            minChunkX,
            minChunkY,
            Math.Max(1, maxChunkX - minChunkX + 1),
            Math.Max(1, maxChunkY - minChunkY + 1),
            startWorldX,
            startWorldY);
    }

    private static int MaxChunkCoord(int worldSize, int gpuChunkSize) =>
        Math.Max(0, (worldSize - 1) / gpuChunkSize);

    private static int ClampChunk(float worldCoord, int gpuChunkSize, int maxChunkCoord) =>
        Math.Clamp((int)MathF.Floor(worldCoord / gpuChunkSize), 0, maxChunkCoord);
}
