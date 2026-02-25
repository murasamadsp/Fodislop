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
            
            // Subscribe to MapManager events to ensure proper initialization
            MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
            MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
        }

        void OnDestroy()
        {
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
            }
            
            // Unsubscribe from MapManager events
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized -= OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;
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
                bool hasMapData = false;
                foreach (var p in hbPacket.Payload)
                {
                    if (p is MapRegionPacket mapRegionPacket)
                    {
                        hasMapData = true;
                        
                        // Ensure MapStorage is initialized before trying to access cellLayer
                        if (MapStorage.Instance.cellLayer == null)
                        {
                            Debug.LogError("MapStorage.cellLayer is null, cannot process map region data");
                            return;
                        }

                        var layer = MapStorage.Instance.cellLayer;
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
                
                // Trigger world data loaded event if we received map data
                if (hasMapData)
                {
                    MapManager.Instance.OnWorldDataLoaded?.Invoke();
                }
            }
            // Add other packet handlers here
        }

        private void OnWorldInitialized()
        {
            Debug.Log("PacketHandler: World initialized event received");
        }

        private void OnWorldDataLoaded()
        {
            Debug.Log("PacketHandler: World data loaded event received");
        }
    }
}
