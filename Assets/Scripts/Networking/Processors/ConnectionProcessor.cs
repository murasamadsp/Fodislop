#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors
{
    public class ConnectionProcessor : IPacketProcessor<DisconnectPacket>, IPacketProcessor<ReconnectPacket>
    {
        public void Process(DisconnectPacket packet)
        {
            ServiceLocator.Resolve<IConnectionService>()?.HandleServerDisconnect(packet.Reason);
        }

        public void Process(ReconnectPacket packet)
        {
            ServiceLocator.Resolve<IConnectionService>()?.HandleServerReconnect();
        }
    }
}
