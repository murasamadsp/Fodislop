#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Networking.Buildings
{
    /// <summary>
    /// Base type of the client-side pack footprints used by the offline server
    /// imitation (DummyConnection). Each pack declares its cell layout and the
    /// anchor-relative roof center manually.
    /// </summary>
    public abstract class PackBuilding
    {
        public abstract PackType Type { get; }

        /// <summary>
        /// Roof center offset from the pack anchor, in world cells
        /// (server axes: X right, Y down). Every pack declares this manually.
        /// </summary>
        public abstract Vector2 RoofCenterOffsetCells { get; }

        public abstract IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace();
    }
}
