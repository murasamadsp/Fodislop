#nullable enable

using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors
{
    public class ConnectionProcessor : IPacketProcessor<DisconnectPacket>, IPacketProcessor<ReconnectPacket>
    {
        private readonly ISessionContainer _session;

        public ConnectionProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(DisconnectPacket packet)
        {
            _session.TryResolve<IConnectionService>()?.HandleServerDisconnect(packet.Reason);
        }

        public void Process(ReconnectPacket packet)
        {
            _session.TryResolve<IConnectionService>()?.HandleServerReconnect();
        }
    }
}
