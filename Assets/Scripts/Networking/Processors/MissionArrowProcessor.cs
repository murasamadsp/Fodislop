#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Networking.Processors;

public sealed class MissionArrowProcessor(IPlayerStats playerStats) : IPacketProcessor<MissionArrowPacket>
{
    public void Process(MissionArrowPacket packet) =>
        playerStats.SetMissionArrow(packet.X, packet.Y);
}
