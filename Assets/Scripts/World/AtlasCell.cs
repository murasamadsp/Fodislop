#nullable enable

using Fodinae.World.Terrain;
using MinesServer.Data;

namespace Fodinae.World;

internal struct AtlasCell
{
    public CellType CellType;
    public Rectangle Rectangle;
    public AtlasCoordinate BaseCoordinate;
}
