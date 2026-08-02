#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class MapRegionProcessor : IPacketProcessor<MapRegionPacket>
    {
        public void Process(MapRegionPacket packet)
        {
            var storage = Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            if (storage?.CellLayer == null || packet.Payload == null)
            {
                return;
            }

            storage.SetRegion(
                packet.X,
                packet.Y,
                packet.Width + 1,
                packet.Height + 1,
                packet.Payload);
        }
    }
}
