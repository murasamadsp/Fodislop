#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

public static class TerrainReliefRimCatalog
{
    public static TerrainRimFamily GetFamily(CellType cellType)
    {
        if (cellType == CellType.Unloaded)
        {
            return TerrainRimFamily.None;
        }

        return MapCellConfigCatalog.GetVisualProperties(cellType).RimFamily;
    }

}
