#nullable enable

namespace Kern.UI;

internal static class MapViewportChunkBudget
{
    internal const int GpuChunkSize = 16;

    internal const int MaxPackedChunkSlots = 16384;

    /// <summary>
    /// The per-cell path can only cover <see cref="MaxPackedChunkSlots"/> visible
    /// chunks; beyond that the viewport must fall back to the mip overview. This
    /// is the only place the decision is made.
    /// </summary>
    internal static bool ShouldUseMip(
        int worldWidth,
        int worldHeight,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY) =>
        MapChunkGrid.Compute(
            worldWidth,
            worldHeight,
            texWidth,
            texHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY).Area > MaxPackedChunkSlots;
}
