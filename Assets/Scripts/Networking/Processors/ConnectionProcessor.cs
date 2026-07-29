#nullable enable

using Fodinae.Networking.Connection;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors
{
    public class ConnectionProcessor : IPacketProcessor<DisconnectPacket>, IPacketProcessor<ReconnectPacket>
    {
        public void Process(DisconnectPacket packet)
        {
            ConnectionManager.Instance?.HandleServerDisconnect(packet.Reason);
        }

        public void Process(ReconnectPacket packet)
        {
            ConnectionManager.Instance?.HandleServerReconnect();
        }
    }
}
