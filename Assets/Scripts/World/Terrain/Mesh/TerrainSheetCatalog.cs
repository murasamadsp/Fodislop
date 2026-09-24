#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

// Совместимый фасад для terrain-кода. Списки типов принадлежат
// CellVisualProtocol, чтобы выбор UV-листа не расходился с остальными
// визуальными свойствами клетки.
public static class TerrainSheetCatalog
{
    public static bool IsContinuousSheet(CellType cellType) =>
        MapCellConfigCatalog.IsContinuousSheet(cellType);

}
