#nullable enable

namespace Fodinae.World;

public static class RenderingConstants
{
    /// <summary>
    /// The size of a single terrain cell in pixels.
    /// </summary>
    public const int CELL_SIZE = 32;

    /// <summary>
    /// First atlas column used by building walls adjacent to corner cells.
    /// </summary>
    public const int BUILDING_WALL_VARIANT_BASE_TILE = 8;

    /// <summary>
    /// Draw order for building roofs, above the doorway overlay.
    /// </summary>
    public const int BUILDING_ROOF_SORTING_ORDER = 600;

    /// <summary>
    /// Pixels Per Unit for sprites and world objects.
    /// </summary>
    public const float PIXELS_PER_UNIT = 16f;
}
