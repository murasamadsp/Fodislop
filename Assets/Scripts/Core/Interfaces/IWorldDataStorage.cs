#nullable enable

using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;

namespace Fodinae.Core.Interfaces
{
    public interface IWorldDataStorage
    {
        bool IsReady { get; }
        WorldLayer<CellType>? CellLayer { get; }
        void SetCell(int x, int y, CellType type);
        CellType GetCell(int x, int y);
        void InitWorld(string worldCodeName, int width, int height);
        void Dispose();
        bool IsInitialized();
        string GetWorldCodeName();
        void EnsureEditorInitialized();
    }
}
