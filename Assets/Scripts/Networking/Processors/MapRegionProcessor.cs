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

            int index = 0;
            for (int y = 0; y <= packet.Height; y++)
            {
                for (int x = 0; x <= packet.Width; x++)
                {
                    if (index < packet.Payload.Length)
                    {
                        storage.SetCell(packet.X + x, packet.Y + y, packet.Payload[index++]);
                    }
                }
            }
        }
    }
}
