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
        // (лава — круглая капля) и смещённая клетка свою клетку целиком не
        // закрывают, и на освободившемся месте под ними не оказывалось
        // ничего: вокруг капель и у сдвинутых масс висела чёрная дыра.
        // Там, где силуэт меньше клетки, подложка обязана остаться.
        return type != foreground || !foregroundFillsCell;
    }
}
