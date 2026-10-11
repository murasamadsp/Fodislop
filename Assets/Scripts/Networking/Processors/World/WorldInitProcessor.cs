#nullable enable

using System.Diagnostics;
using Kern.Core.Interfaces;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Processors;

public sealed class WorldInitProcessor : System.IDisposable
{
    private readonly IMapDataProvider _mapManager;
    private readonly IWorldReadiness _gameManager;
    private readonly IRobotService _robotManager;
    private readonly IBuildingService _buildingManager;
    private readonly ILocalPlayerState _localPlayer;

    public WorldInitProcessor(
        IMapDataProvider mapManager,
        IWorldReadiness gameManager,
        IRobotService robotManager,
        IBuildingService buildingManager,
        ILocalPlayerState localPlayer)
    {
        _mapManager = mapManager;
        _gameManager = gameManager;
        _robotManager = robotManager;
        _buildingManager = buildingManager;
        _localPlayer = localPlayer;
        mapManager.OnWorldInitialized += gameManager.NotifyWorldLoaded;
    }

    public void Process(WorldInitPacket packet)
    {
        long started = Stopwatch.GetTimestamp();
        _robotManager.ClearAllRobots();
        long robotsCleared = Stopwatch.GetTimestamp();
        _buildingManager.ClearAllBuildings();
        long buildingsCleared = Stopwatch.GetTimestamp();
        _localPlayer.Current?.ResetServerPosition();
        long playerReset = Stopwatch.GetTimestamp();
        _mapManager.LoadWorldInit(packet);
        long worldLoaded = Stopwatch.GetTimestamp();

        UnityEngine.Debug.Log(
            $"[WorldInit] stages ms: robots={(robotsCleared - started) * 1000d / Stopwatch.Frequency:F1}, " +
            $"buildings={(buildingsCleared - robotsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"player={(playerReset - buildingsCleared) * 1000d / Stopwatch.Frequency:F1}, " +
            $"map={(worldLoaded - playerReset) * 1000d / Stopwatch.Frequency:F1}, " +
            $"total={(worldLoaded - started) * 1000d / Stopwatch.Frequency:F1}.");
    }

    public void Dispose()
    {
        _mapManager.OnWorldInitialized -= _gameManager.NotifyWorldLoaded;
    }
}
