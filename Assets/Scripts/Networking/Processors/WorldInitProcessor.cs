#nullable enable

using Fodinae.Core;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
    {
        public void Process(WorldInitPacket packet)
        {
            Debug.Log("[WorldInitProcessor] Processing WorldInitPacket");
            var mm = Fodinae.Core.ServiceLocator.Resolve<MapManager>();
            if (mm == null)
            {
                Debug.LogError("[WorldInitProcessor] MapManager is null — cannot process WorldInitPacket");
                return;
            }

            mm.LoadWorldInit(packet);
        }
    }
}
