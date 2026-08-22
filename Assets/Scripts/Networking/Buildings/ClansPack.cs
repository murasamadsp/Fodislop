#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Footprint copied 1:1 from MinesServer Game/Buildings/ClansPack.cs (CellsToPlace).
    /// </summary>
    public sealed class ClansPack : PackBuilding
    {
        public override PackType Type => PackType.Clans;

        public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
        {
            yield return ((0, 0), CellType.BuildingDoor);
            yield return ((0, 1), CellType.BuildingDoor);
            yield return ((0, 2), CellType.BuildingRoad);
            yield return ((0, 3), CellType.BuildingRoad);
            yield return ((0, 4), CellType.BuildingRoad);
            yield return ((-2, -1), CellType.BuildingCorner);
            yield return ((-1, -1), CellType.BuildingWall);
            yield return ((0, -1), CellType.BuildingWall);
            yield return ((1, -1), CellType.BuildingWall);
            yield return ((2, -1), CellType.BuildingCorner);
            yield return ((-2, 2), CellType.BuildingCorner);
            yield return ((-1, 2), CellType.BuildingWall);
            yield return ((1, 2), CellType.BuildingWall);
            yield return ((2, 2), CellType.BuildingCorner);
            for (int i = -2; i <= 2; i++)
            {
                if (i != 0)
                {
                    yield return ((i, 0), CellType.BuildingWall);
                    yield return ((i, 1), CellType.BuildingWall);
                }
            }
        }
    }
}
