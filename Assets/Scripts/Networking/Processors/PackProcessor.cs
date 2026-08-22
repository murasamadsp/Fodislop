#nullable enable

using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class PackProcessor : IPacketProcessor<PackPacket>, IPacketProcessor<RemovePackPacket>
    {
        private readonly ISessionContainer _session;

        public PackProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(PackPacket packet)
        {
            _session.TryResolve<PackManager>()?.AddOrUpdatePack(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);
        }

        public void Process(RemovePackPacket packet)
        {
            _session.TryResolve<PackManager>()?.RemovePack(packet.X, packet.Y);
        }
    }
}
