#nullable enable

using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;

namespace Fodinae.Core.Interfaces
{
    public interface IWorldDataStorage
    {
        bool IsReady { get; }
        long Revision { get; }
        WorldLayer<CellType>? CellLayer { get; }
        void SetCell(int x, int y, CellType type);
        void SetRegion(int startX, int startY, int width, int height, CellType[] cells);
        CellType GetCell(int x, int y);
        void InitWorld(string worldCodeName, int width, int height);
        void Dispose();
        bool IsInitialized();
        string GetWorldCodeName();
        void EnsureEditorInitialized();
    }
}
