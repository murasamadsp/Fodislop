#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Buildings;
public abstract class PackBuilding
{
    public abstract PackType Type { get; }

    public abstract IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace();
}
