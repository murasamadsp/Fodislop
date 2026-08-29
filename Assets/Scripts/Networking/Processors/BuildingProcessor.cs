#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class BuildingProcessor : IPacketProcessor<PackPacket>, IPacketProcessor<RemovePackPacket>
    {
        private readonly IBuildingService _buildingManager;

        public BuildingProcessor(IBuildingService buildingManager)
        {
            _buildingManager = buildingManager;
        }

        public void Process(PackPacket packet)
        {
            _buildingManager.AddOrUpdateBuilding(packet.X, packet.Y, packet.PackCode, packet.Variant, packet.LinkedClan);
        }

        public void Process(RemovePackPacket packet)
        {
            _buildingManager.RemoveBuilding(packet.X, packet.Y);
        }
    }
}
