#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

// Layer ownership is decided before geometry is built. Empty ground belongs
// to the rectangular background, even when it came from foreground map data.
internal static class TerrainCellLayers
{
    // Что лежит под клеткой. Пол лежит сам на себе, проходимая часть пака —
    // на дороге, всё остальное — на обычной земле.
    public static CellType ResolveBackground(CellType foreground, CellConfigProperties properties)
    {
        if (foreground == CellType.Unloaded || (properties & CellConfigProperties.Passable) == 0)
        {
            return CellType.Empty;
        }

        return MapCellConfigCatalog.GetVisualProperties(foreground).IsBuilding
            ? CellType.Road
            : foreground;
    }

    public static bool TryGetType(
        CellType foreground,
        CellType background,
        bool isBackground,
        bool foregroundFillsCell,
        out CellType type)
    {
        if (foreground == CellType.Empty)
        {
            type = CellType.Empty;
            return isBackground;
        }

        type = isBackground ? background : foreground;
        if (!isBackground)
        {
            return true;
        }

        if (type == CellType.Unloaded)
        {
            return false;
        }

        // Совпадение типов роняет фоновый квад: подложка под сплошной клеткой
        // не видна ни в одном пикселе, и рисовать её незачем.
        //
        // Но «сплошная» — это про силуэт, а не про тип. Скруглённая клетка
        // (лава — круглая капля) свою клетку целиком не закрывает, и на
        // освободившемся месте под ней не оказывалось ничего: вокруг каждой
        // капли висел чёрный ореол. Там, где силуэт меньше клетки, подложка
        // обязана остаться.
        return type != foreground || !foregroundFillsCell;
    }
}
