#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

// Семьи клеток по участию в кайме.
//
// В оригинале кайму получают только `crysy` и `rocky` — те, у кого
// textureType == 1. `blocky` лежит в одной рельефной семье со скалами
// (reliefType = 2), но каймы не имеет, поэтому вывести участие из рельефной
// группы нельзя: по группе блоки от скал неотличимы.
//
// Сейчас кайму получают все клетки переднего плана. Перечисление оставлено
// ровно затем, чтобы исключать семьи по одной, не трогая ни шейдер, ни
// транспорт: достаточно вернуть false для нужной ветки.
public enum TerrainRimFamily : byte
{
    None = 0,
    Crystal = 1,
    Rock = 2,
    Block = 3,
    Sand = 4,
    Ground = 5,
    Other = 6,
}

public static class TerrainReliefRimCatalog
{
    // Все семьи участвуют. Отсюда и настраивается состав каймы.
    public static bool ParticipatesInRim(TerrainRimFamily family) => family switch
    {
        TerrainRimFamily.Crystal => true,
        TerrainRimFamily.Rock => true,
        TerrainRimFamily.Block => true,
        TerrainRimFamily.Sand => true,
        TerrainRimFamily.Ground => true,
        TerrainRimFamily.Other => true,
        _ => false,
    };

    public static bool ParticipatesInRim(CellType cellType) =>
        ParticipatesInRim(GetFamily(cellType));

    public static TerrainRimFamily GetFamily(CellType cellType)
    {
        if (cellType == CellType.Unloaded)
        {
            return TerrainRimFamily.None;
        }

        if (TerrainSheetCatalog.IsContinuousSheet(cellType))
        {
            return IsRock(cellType) ? TerrainRimFamily.Rock : TerrainRimFamily.Crystal;
        }

        if (MapCellConfigCatalog.IsBuildingOrArtificialBlock(cellType))
        {
            return TerrainRimFamily.Block;
        }

        if (TerrainDecalCatalog.GetFamily(cellType) == TerrainDecalFamily.Sand)
        {
            return TerrainRimFamily.Sand;
        }

        return TerrainDecalCatalog.IsGroundSurface(cellType)
            ? TerrainRimFamily.Ground
            : TerrainRimFamily.Other;
    }

    private static bool IsRock(CellType cellType) => cellType is
        CellType.Rock or
        CellType.HeavyRock or
        CellType.DeepRock or
        CellType.GRock or
        CellType.GoldenRock or
        CellType.DeepObsidianRock or
        CellType.DeepStripedRock or
        CellType.RedRock or
        CellType.NiggerRock or
        CellType.LivingBlackRock;
}
