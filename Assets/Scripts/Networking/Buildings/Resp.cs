#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Footprint copied 1:1 from MinesServer Game/Buildings/Resp.cs (CellsToPlace).
    /// </summary>
    public sealed class Resp : PackBuilding
    {
        public override PackType Type => PackType.Resp;

        public override Vector2 RoofCenterOffsetCells => new(0f, 0.5775f);

        public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
        {
            yield return ((0, 0), CellType.BuildingDoor);
            yield return ((1, 0), CellType.BuildingDoor);
            yield return ((-1, 0), CellType.BuildingWall);
            yield return ((0, -1), CellType.BuildingWall);
            yield return ((0, 1), CellType.BuildingWall);
            yield return ((1, 1), CellType.BuildingWall);
            yield return ((-1, 1), CellType.BuildingWall);
            yield return ((1, -1), CellType.BuildingWall);
            yield return ((-1, -1), CellType.BuildingWall);
            yield return ((1, 2), CellType.BuildingWall);
            yield return ((-1, 2), CellType.BuildingWall);
            yield return ((0, 2), CellType.BuildingDoor);
            yield return ((0, 3), CellType.BuildingRoad);
            yield return ((0, 4), CellType.BuildingRoad);
            for (int xx = 2; xx < 6; xx++)
            {
                for (int yy = -1; yy < 3; yy++)
                {
                    yield return ((xx, yy), CellType.BuildingRoad);
                }
            }
        }
    }
}
