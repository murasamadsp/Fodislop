#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class BuildingProcessor :
    IPacketProcessor<BuildingPacket>,
    IPacketProcessor<RemoveBuildingPacket>
{
    private readonly IBuildingService _buildingManager;

    public BuildingProcessor(IBuildingService buildingManager)
    {
        _buildingManager = buildingManager ?? throw new ArgumentNullException(nameof(buildingManager));
    }

    public void Process(BuildingPacket packet) =>
        _buildingManager.AddOrUpdateBuilding(packet.X, packet.Y, packet.BuildingCode, packet.Variant, packet.LinkedClan);

    public void Process(RemoveBuildingPacket packet) =>
        _buildingManager.RemoveBuilding(packet.X, packet.Y);
}
