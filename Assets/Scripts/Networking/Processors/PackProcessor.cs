#nullable enable

using Fodinae.Core;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class PackProcessor : IPacketProcessor<PackPacket>, IPacketProcessor<RemovePackPacket>
    {
        public void Process(PackPacket packet)
        {
            Fodinae.Core.ServiceLocator.Resolve<PackManager>()?.AddOrUpdatePack(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);
        }

        public void Process(RemovePackPacket packet)
        {
            Fodinae.Core.ServiceLocator.Resolve<PackManager>()?.RemovePack(packet.X, packet.Y);
        }
    }
}
