using Fodinae.Scripts.World.Terrain;
using MinesServer.Data;

namespace Fodinae.Scripts.World
{
    internal struct AtlasCell
    {
        public CellType CellType;
        public Rectangle Rectangle;
        public AtlasCoordinate BaseCoordinate;
    }
}
