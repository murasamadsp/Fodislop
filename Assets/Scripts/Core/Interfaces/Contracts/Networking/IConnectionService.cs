#nullable enable

using System;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Server.Packets;

namespace Kern.Core.Interfaces;

public sealed class ConnectionStartupInterruptedException : InvalidOperationException
{
    public ConnectionStartupInterruptedException()
        : base("Server connection was lost during world startup.")
    {
    }
}

public interface IConnectionService
{
    bool IsConnected { get; }
    bool IsAutoReconnecting { get; }
    bool IsWorldStartupPending { get; }
    bool IsOffline { get; }
    void Connect(bool oldClient = false);
    void Disconnect();
    void MarkWorldReady();
    void TriggerDisconnect(string reason);
    void TriggerReconnect(string reason);
    void HandleServerDisconnect(string reason);
    void HandleServerReconnect();
    void Send(ClientPacket packet);
    event Action<ServerPacket>? OnPacketReceived;
    event Action? OnPacketBatchStarted;
    event Action? OnPacketBatchCompleted;
    event Action? OnTransportDisconnected;
    event Action<string>? OnReconnectStatusChanged;
    event Action<string>? OnDisconnectReason;
    event Action? OnReconnectHidden;
}

public interface IOfflineConnection
{
    void TriggerDisconnect(string reason);
    void TriggerReconnect(string reason);
}

// Optional transport capability; coordinates are server cells, Y down.
public interface IWorldRegionRequester
{
    void RequestWorldRegion(string worldCodeName, UnityEngine.RectInt serverRegion);
}
