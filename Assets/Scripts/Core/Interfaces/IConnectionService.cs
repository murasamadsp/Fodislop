#nullable enable

using System;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Server.Packets;

namespace Fodinae.Core.Interfaces
{
    public interface IConnectionService
    {
        bool IsConnected { get; }
        bool IsOffline { get; }
        void Connect(bool oldClient = false);
        void Disconnect();
        void TriggerDisconnect(string reason);
        void TriggerReconnect(string reason);
        void HandleServerDisconnect(string reason);
        void HandleServerReconnect();
        void Send(ClientPacket packet);
        event Action<ServerPacket>? OnPacketReceived;
    }

    public interface IOfflineConnection
    {
        void TriggerDisconnect(string reason);
        void TriggerReconnect(string reason);
    }
}
