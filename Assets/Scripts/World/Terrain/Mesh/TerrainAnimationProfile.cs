#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

internal enum TerrainAnimationProfile : byte
{
    Default = 0,
    PrismaticCrystal = 1,
    MoltenSurface = 2,
    FacetedCrystal = 3,
}

internal readonly record struct TerrainAnimationSettings(
    TerrainAnimationProfile Profile,
    float Speed,
    float PaletteIndex = 0f);

internal static class TerrainAnimationProfileCatalog
{
    // Shader converts this legacy speed unit to radians per second (x 0.05).
    private const float PrismaticCrystalSpeed = 50f;
    private const float FacetedCrystalSpeed = 0.06f;

    public static TerrainAnimationSettings Get(CellType cellType, float configuredSpeed)
    {
        if (cellType is
            CellType.XGreen or
            CellType.XBlue or
            CellType.XRed or
            CellType.XCyan or
            CellType.XViolet)
        {
            return new TerrainAnimationSettings(
                TerrainAnimationProfile.PrismaticCrystal,
                PrismaticCrystalSpeed,
                cellType switch
                {
                    CellType.XGreen => 1f,
                    CellType.XBlue => 2f,
                    CellType.XRed => 3f,
                    CellType.XViolet => 4f,
                    CellType.XCyan => 5f,
                    _ => throw new System.ArgumentOutOfRangeException(nameof(cellType)),
                });
        }

        if (cellType == CellType.Lava)
        {
            return new TerrainAnimationSettings(
                TerrainAnimationProfile.MoltenSurface,
                configuredSpeed);
        }

        if (cellType is
            CellType.Green or
            CellType.Red or
            CellType.Blue or
            CellType.Violet or
            CellType.White or
            CellType.Cyan)
        {
            return new TerrainAnimationSettings(
                TerrainAnimationProfile.FacetedCrystal,
                FacetedCrystalSpeed);
        }

        return new TerrainAnimationSettings(
            TerrainAnimationProfile.Default,
            configuredSpeed);
    }
}
