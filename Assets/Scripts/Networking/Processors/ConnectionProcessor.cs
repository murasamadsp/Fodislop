#nullable enable

using Fodinae.Scripts.Networking.Connection;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Scripts.Networking.Processors
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
