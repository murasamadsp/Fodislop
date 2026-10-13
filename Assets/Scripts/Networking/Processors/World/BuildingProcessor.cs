#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class BuildingProcessor(IBuildingService buildingManager) :
    IPacketProcessor<BuildingPacket>,
    IPacketProcessor<RemoveBuildingPacket>
{
    public void Process(BuildingPacket packet) =>
        buildingManager.AddOrUpdateBuilding(packet.X, packet.Y, packet.BuildingCode, packet.Variant, packet.LinkedClan);

    public void Process(RemoveBuildingPacket packet) =>
        buildingManager.RemoveBuilding(packet.X, packet.Y);
}
