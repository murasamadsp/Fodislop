#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>Где и с какой частотой ставится декаль; Percent == 0 — нигде.</summary>
public readonly record struct TerrainDecalRule(uint Percent, uint Seed, bool RockAtlas);

public static class TerrainDecalCatalog
{
    public const int VariantCount = 16;

    // Доля клеток камня, получающих декаль. Порог сравнивается с хэшем
    // клетки, поэтому подъём доли только добавляет декали, не трогая уже
    // стоящие.
    private const uint RockPlacementPercent = 30;

    // Бит 12 (= 4096) в упаковке сигнализирует шейдеру использовать
    // _TerrainDecalRockAtlas вместо основного _TerrainDecalAtlas.
    // Именно 12, а не 11: раскладка упирается ровно в 2048
    // (1 + 15 + (3 << 4) + 64 + (3 << 7) + (3 << 9)), поэтому бит 11
    // выставлялся бы у самой старшей декали.
    private const int RockAtlasBit = 1 << 12;

    // Декаль земли и камня одним правилом: процент, зерно хэша и атлас.
    // Правило лежит в строке типа, и шейдер ставит декаль тем же хэшем, что
    // и Place, — поэтому сама декаль в данных клетки не хранится.
    public static readonly TerrainDecalRule GroundRule =
        new(TerrainConfigHolder.GroundDecalPlacementPercent, (uint)CellType.Empty, RockAtlas: false);

    public static readonly TerrainDecalRule RockRule =
        new(RockPlacementPercent, Seed: 7u, RockAtlas: true);

    // Фон — земля под любым загруженным типом; передний план — по семье из
    // cells.json (decalFamily).
    public static TerrainDecalRule GetSurfaceRule(CellType cellType, bool isBackground)
    {
        if (isBackground)
        {
            return cellType != CellType.Unloaded ? GroundRule : default;
        }

        return RuleOf(GetFamily(cellType));
    }

    public static TerrainDecalRule RuleOf(TerrainDecalFamily family) => family switch
    {
        TerrainDecalFamily.Ground => GroundRule,
        TerrainDecalFamily.Rock => RockRule,
        _ => default,
    };

    public static TerrainDecalFamily GetFamily(CellType cellType) =>
        MapCellConfigCatalog.GetVisualProperties(cellType).DecalFamily;

    public static int Place(TerrainDecalRule rule, int worldX, int serverY)
    {
        if (rule.Percent == 0u)
        {
            return 0;
        }

        uint hash = Hash(worldX, serverY, rule.Seed);
        if ((hash % 100u) >= rule.Percent)
        {
            return 0;
        }

        int variant = (int)(hash % (uint)VariantCount);
        int rotation = (int)((hash >> 8) & 3u);
        int mirror = (int)((hash >> 10) & 1u);
        int offsetX = (int)((hash >> 12) & 3u);
        int offsetY = (int)((hash >> 14) & 3u);
        int packed = 1 + variant + (rotation << 4) + (mirror << 6) +
            (offsetX << 7) + (offsetY << 9);
        return rule.RockAtlas ? packed | RockAtlasBit : packed;
    }

    private static uint Hash(int worldX, int serverY, uint seed)
    {
        uint hash = unchecked((uint)worldX) * 374761393u;
        hash += unchecked((uint)serverY) * 668265263u;
        hash ^= seed * 2246822519u;
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return hash ^ (hash >> 16);
    }
}
