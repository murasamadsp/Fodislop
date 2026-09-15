#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors;

public sealed class WorldInitProcessor : System.IDisposable
{
    private readonly IMapDataProvider _mapManager;
    private readonly IWorldReadiness _gameManager;

    public WorldInitProcessor(IMapDataProvider mapManager, IWorldReadiness gameManager)
    {
        _mapManager = mapManager;
        _gameManager = gameManager;
        mapManager.OnWorldInitialized += gameManager.NotifyWorldLoaded;
    }

    public void Process(WorldInitPacket packet)
    {
        _mapManager.LoadWorldInit(packet);
    }

    public void Dispose()
    {
        _mapManager.OnWorldInitialized -= _gameManager.NotifyWorldLoaded;
    }
}
