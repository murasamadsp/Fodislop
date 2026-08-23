#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Custom 5x4 footprint (user-specified layout, no road tail):
    /// corners in the outer column edges of the first and last rows,
    /// double door entrance at the center of the bottom half.
    ///   row y=-2: corner wall wall wall corner
    ///   row y=-1: wall wall wall wall wall
    ///   row y=0:  wall wall door wall wall
    ///   row y=+1: corner wall door wall corner
    /// </summary>
    public sealed class ClansPack : PackBuilding
    {
        public override PackType Type => PackType.Clans;

        public override Vector2 RoofCenterOffsetCells => new(0f, -0.5f);

        public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
        {
            // Row 1 (y=-2): corner wall wall wall corner
            yield return ((-2, -2), CellType.BuildingCorner);
            yield return ((-1, -2), CellType.BuildingWall);
            yield return ((0, -2), CellType.BuildingWall);
            yield return ((1, -2), CellType.BuildingWall);
            yield return ((2, -2), CellType.BuildingCorner);

            // Row 2 (y=-1): full wall line
            for (int x = -2; x <= 2; x++)
            {
                yield return ((x, -1), CellType.BuildingWall);
            }

            // Row 3 (y=0): walls with the center door
            yield return ((-2, 0), CellType.BuildingWall);
            yield return ((-1, 0), CellType.BuildingWall);
            yield return ((0, 0), CellType.BuildingDoor);
            yield return ((1, 0), CellType.BuildingWall);
            yield return ((2, 0), CellType.BuildingWall);

            // Row 4 (y=+1): corner wall door wall corner
            yield return ((-2, 1), CellType.BuildingCorner);
            yield return ((-1, 1), CellType.BuildingWall);
            yield return ((0, 1), CellType.BuildingDoor);
            yield return ((1, 1), CellType.BuildingWall);
            yield return ((2, 1), CellType.BuildingCorner);
        }
    }
}
