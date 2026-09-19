#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

internal enum TerrainAnimationProfile : byte
{
    Default = 0,
    PrismaticCrystal = 1,
}

internal readonly record struct TerrainAnimationSettings(
    TerrainAnimationProfile Profile,
    float Speed);

internal static class TerrainAnimationProfileCatalog
{
    // Matches the pace of the original X-crystal effect. The shader applies
    // the shared shimmer time scale, so this is not an angular velocity.
    private const float PrismaticCrystalSpeed = 50f;

    public static TerrainAnimationSettings Get(CellType cellType, float configuredSpeed)
    {
        return cellType is
            CellType.XGreen or
            CellType.XBlue or
            CellType.XRed or
            CellType.XCyan or
            CellType.XViolet
            ? new TerrainAnimationSettings(
                TerrainAnimationProfile.PrismaticCrystal,
                PrismaticCrystalSpeed)
            : new TerrainAnimationSettings(
                TerrainAnimationProfile.Default,
                configuredSpeed);
    }
}
