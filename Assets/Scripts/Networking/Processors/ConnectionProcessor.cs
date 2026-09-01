#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors
{
    public class ConnectionProcessor : IPacketProcessor<DisconnectPacket>, IPacketProcessor<ReconnectPacket>
    {
        private readonly IConnectionService _connection;

        public ConnectionProcessor(IConnectionService connection)
        {
            _connection = connection;
        }

        public void Process(DisconnectPacket packet)
        {
            _connection.HandleServerDisconnect(packet.Reason);
        }

        public void Process(ReconnectPacket packet)
        {
            _connection.HandleServerReconnect();
        }
    }
}
