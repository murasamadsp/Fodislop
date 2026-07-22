using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    /// <summary>
    /// Decoupled SOLID Processor for World Map Region Chunk Packets.
    /// Manages RLE chunk decoding, MapStorage updates, and map region load notifications.
    /// </summary>
    public class MapRegionProcessor : IPacketProcessor<MapRegionPacket>
    {
        public void Process(MapRegionPacket packet)
        {
            if (MapStorage.Instance == null || MapStorage.Instance.CellLayer == null || packet.Payload == null)
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
                        MapStorage.Instance.SetCell(packet.X + x, packet.Y + y, packet.Payload[index++]);
                    }
                }
            }
        }
    }
}
