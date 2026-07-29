#nullable enable

using System;
using MinesServer.Networking.Server.Packets;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IConnectionService
    {
        bool IsConnected { get; }
        void Connect(bool oldClient = false);
        void Disconnect();
        event Action<ServerPacket> OnPacketReceived;
    }
}
