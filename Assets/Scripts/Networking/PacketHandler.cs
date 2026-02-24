using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Networking.Connection;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Assets.Scripts.Networking
{
    public class PacketHandler : MonoBehaviour
    {
        void Start()
        {
            ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
        }

        void OnDestroy()
        {
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
            }
        }

        private void OnPacketReceived(ServerPacket packet)
        {
            if (packet.Payload is WorldInitPacket worldInitPacket)
            {
                MapManager.Instance.LoadWorldInit(worldInitPacket);
            }
            else if (packet.Payload is HBPacket hbPacket)
            {
                foreach (var p in hbPacket.Payload)
                {
                    if (p is MapRegionPacket mapRegionPacket)
                    {
                        var layer = MapStorage.Instance.cellLayer;
                        if (layer == null) return;

                        int index = 0;
                        for (int y = 0; y <= mapRegionPacket.Height; y++)
                        {
                            for (int x = 0; x <= mapRegionPacket.Width; x++)
                            {
                                layer[mapRegionPacket.X + x, mapRegionPacket.Y + y] = mapRegionPacket.Payload[index++];
                            }
                        }
                    }
                }
            }
            // Add other packet handlers here
        }
    }
}
