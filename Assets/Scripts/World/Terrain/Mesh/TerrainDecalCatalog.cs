#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

public enum TerrainDecalFamily : byte
{
    None = 0,
    Stone = 1,
    Sand = 2,
    Road = 3,
}

public static class TerrainDecalCatalog
{
    private const uint PlacementPercent = 22;

    public static int GetPackedPlacement(CellType cellType, int worldX, int serverY)
    {
        TerrainDecalFamily family = GetFamily(cellType);
        if (family == TerrainDecalFamily.None)
        {
            return 0;
        }

        uint hash = Hash(worldX, serverY, (uint)cellType);
        if ((hash % 100u) >= PlacementPercent)
        {
            return 0;
        }

        int variant = family switch
        {
            TerrainDecalFamily.Stone => (hash & 1u) == 0u ? 0 : 2,
            TerrainDecalFamily.Sand => (hash & 1u) == 0u ? 1 : 3,
            TerrainDecalFamily.Road => (int)(hash % 3u),
            _ => 0,
        };
        int rotation = (int)((hash >> 8) & 3u);
        int mirror = (int)((hash >> 10) & 1u);
        return 1 + variant + (rotation << 2) + (mirror << 4);
    }

    public static TerrainDecalFamily GetFamily(CellType cellType)
    {
        if (cellType is
            CellType.BlackBoulder1 or
            CellType.BlackBoulder2 or
            CellType.BlackBoulder3 or
            CellType.MetalBoulder1 or
            CellType.MetalBoulder2 or
            CellType.MetalBoulder3 or
            CellType.DeepObsidianRock or
            CellType.DeepStripedRock or
            CellType.Boulder1 or
            CellType.Boulder2 or
            CellType.Boulder3 or
            CellType.Rock or
            CellType.HeavyRock or
            CellType.NiggerRock or
            CellType.RedRock or
            CellType.GoldenRock or
            CellType.DeepRock or
            CellType.GRock)
        {
            return TerrainDecalFamily.Stone;
        }

        if (cellType is
            CellType.WhiteSand or
            CellType.DarkWhiteSand or
            CellType.RustySand or
            CellType.DarkRustySand or
            CellType.BlackSand or
            CellType.DarkBlackSand or
            CellType.BlueSand or
            CellType.DarkBlueSand or
            CellType.YellowSand or
            CellType.DarkYellowSand)
        {
            return TerrainDecalFamily.Sand;
        }

        return cellType == CellType.Road
            ? TerrainDecalFamily.Road
            : TerrainDecalFamily.None;
    }

    private static uint Hash(int worldX, int serverY, uint cellType)
    {
        uint hash = unchecked((uint)worldX) * 374761393u;
        hash += unchecked((uint)serverY) * 668265263u;
        hash ^= cellType * 2246822519u;
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return hash ^ (hash >> 16);
    }
}
