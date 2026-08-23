#nullable enable

namespace Fodinae.World;

public static class RenderingConstants
{
    /// <summary>
    /// The size of a single terrain cell in pixels.
    /// </summary>
    public const int CELL_SIZE = 32;

    /// <summary>
    /// Atlas tile column of the first building-wall neighbor variant in
    /// Cells/106.png: column 8 (x=256px) has no adjacent corner cells,
    /// column 9 one corner, column 10 two corners.
    /// </summary>
    public const int BUILDING_WALL_VARIANT_BASE_TILE = 8;

    /// <summary>
    /// Sorting order of pack roof sprites. Must stay ABOVE the terrain
    /// door-overlay mesh (TerrainRenderer._overlaySortingOrder, default 500)
    /// so doorway tiles do not paint over the building roof.
    /// </summary>
    public const int PACK_ROOF_SORTING_ORDER = 600;

    /// <summary>
    /// Pixels Per Unit for sprites and world objects.
    /// </summary>
    public const float PIXELS_PER_UNIT = 16f;
}
