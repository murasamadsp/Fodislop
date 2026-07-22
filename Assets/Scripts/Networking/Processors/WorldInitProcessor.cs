using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    /// <summary>
    /// Decoupled SOLID Processor for World Initialization Packets.
    /// Manages world dimensions, cell configurations, and map manager bootstrap.
    /// </summary>
    public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
    {
        public void Process(WorldInitPacket packet)
        {
            Debug.Log($"[WorldInitProcessor] Processing WorldInit: width={packet.Width}, height={packet.Height}");

            if (MapManager.Instance != null)
            {
                MapManager.Instance.LoadWorldInit(packet);
            }
        }
    }
}
