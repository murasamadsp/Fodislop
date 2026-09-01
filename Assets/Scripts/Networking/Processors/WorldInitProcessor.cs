#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors;

/// <summary>
/// Applies WorldInitPacket to the world domain: loads the world into
/// MapStorage/MapManager and routes the manager's world-initialized signal to
/// GameManager so world readiness is published from one place.
/// </summary>
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
