#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors;

public sealed class ConnectionProcessor(IConnectionService connection) :
    IPacketProcessor<DisconnectPacket>,
    IPacketProcessor<ReconnectPacket>
{
    public void Process(DisconnectPacket packet) =>
        connection.HandleServerDisconnect(packet.Reason);

    public void Process(ReconnectPacket packet) =>
        connection.HandleServerReconnect();
}
