#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

internal readonly record struct TerrainAnimationSettings(
    TerrainAnimationProfile Profile,
    float Speed,
    float PaletteIndex = 0f);

internal static class TerrainAnimationProfileCatalog
{
    // Профиль и палитра — из поверхности типа (cells.json: surface,
    // surfacePalette); скорость профиля — авторская настройка.
    public static TerrainAnimationSettings Get(CellType cellType, float configuredSpeed)
    {
        CellVisualProperties visuals = MapCellConfigCatalog.GetVisualProperties(cellType);
        return visuals.SurfaceProfile switch
        {
            TerrainAnimationProfile.PrismaticCrystal => new TerrainAnimationSettings(
                TerrainAnimationProfile.PrismaticCrystal,
                TerrainConfigHolder.PrismaticCrystalAnimationSpeed,
                visuals.PaletteIndex),
            TerrainAnimationProfile.FacetedCrystal => new TerrainAnimationSettings(
                TerrainAnimationProfile.FacetedCrystal,
                TerrainConfigHolder.FacetedCrystalAnimationSpeed),
            TerrainAnimationProfile.MoltenSurface => new TerrainAnimationSettings(
                TerrainAnimationProfile.MoltenSurface,
                TerrainConfigHolder.MoltenSurfaceAnimationSpeed),
            _ => new TerrainAnimationSettings(TerrainAnimationProfile.Default, configuredSpeed),
        };
    }
}
