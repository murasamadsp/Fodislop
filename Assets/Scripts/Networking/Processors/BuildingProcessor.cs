#nullable enable

using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class BuildingProcessor : IPacketProcessor<PackPacket>, IPacketProcessor<RemovePackPacket>
    {
        private readonly ISessionContainer _session;

        public BuildingProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(PackPacket packet)
        {
            _session.TryResolve<BuildingManager>()?.AddOrUpdateBuilding(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);
        }

        public void Process(RemovePackPacket packet)
        {
            _session.TryResolve<BuildingManager>()?.RemoveBuilding(packet.X, packet.Y);
        }
    }
}
