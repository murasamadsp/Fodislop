#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

// Layer ownership is decided before geometry is built. Empty ground belongs
// to the rectangular background, even when it came from foreground map data.
internal static class TerrainCellLayers
{
    public static bool TryGetType(
        CellType foreground,
        CellType background,
        bool isBackground,
        out CellType type)
    {
        if (foreground == CellType.Empty)
        {
            type = CellType.Empty;
            return isBackground;
        }

        type = isBackground ? background : foreground;
        return !isBackground || (type != foreground && type != CellType.Unloaded);
    }
}
