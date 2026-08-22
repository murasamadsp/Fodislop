#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Client-side mirror of the authoritative MinesServer building footprint
    /// contract (Game/Buildings/Actions/IPlaceable.CellsToPlace). Every
    /// placeable pack owns a static set of world cells relative to its anchor
    /// cell; the per-role CellTypes (wall/corner/door/road) drive both gameplay
    /// and the client renderer.
    /// </summary>
    public abstract class PackBuilding
    {
        public abstract PackType Type { get; }

        public abstract IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace();
    }
}
