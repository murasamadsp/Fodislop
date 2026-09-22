#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

// Клетки, которые рисуются одним сплошным листом, а не тайлом на клетку.
//
// В оригинале это признак textureType == 1, и ставят его ровно два списка:
// crysy и rocky (CellRender.cs:165, 180). Кристаллы и камень — единственные,
// кто адресует атлас по мировой координате целиком; пески идут веткой
// автотайлинга по соседям, блоки — своей.
//
// Смысл в том, что у искажённой клетки выборка обязана перетечь за край
// своего тайла в соседний, а соседний тайл — соседний кусок той же картинки.
// Массив тогда читается одним выломанным камнем, а не кладкой из штампов.
// Тайл на клетку этого не умеет: его UV прибит к клетке, геометрия уезжает
// без него, и на каждой границе остаётся шов.
//
// Список, а не признак из конфигурации: в протоколе такого поля нет, а
// рельефная группа для этого не годится — в оригинале блоки лежат в одной
// рельефной семье с камнем, но листом не рисуются.
public static class TerrainSheetCatalog
{
    public static bool IsContinuousSheet(CellType cellType) =>
        IsCrystal(cellType) || IsRock(cellType);

    private static bool IsCrystal(CellType cellType) => cellType is
        CellType.XGreen or
        CellType.XBlue or
        CellType.XRed or
        CellType.XCyan or
        CellType.XViolet or
        CellType.Green or
        CellType.Red or
        CellType.Blue or
        CellType.Violet or
        CellType.White or
        CellType.Cyan or
        CellType.AliveCyan or
        CellType.AliveRed or
        CellType.AliveViol or
        CellType.AliveNigger or
        CellType.AliveWhite or
        CellType.AliveRainbow or
        CellType.AliveBlue or
        CellType.Pearl or
        CellType.SuperRainbow or
        CellType.HypnoRock or
        CellType.AcidRock or
        CellType.DeepTurquoiseRock or
        CellType.DeepRainbowRock or
        CellType.DeepLazuriteSand;

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
