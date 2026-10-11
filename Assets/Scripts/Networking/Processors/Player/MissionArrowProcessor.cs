#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Kern.Networking.Processors;

public sealed class MissionArrowProcessor : IPacketProcessor<MissionArrowPacket>
{
    private readonly IPlayerStats _playerStats;
    public MissionArrowProcessor(IPlayerStats playerStats)
    {
        _playerStats = playerStats;
    }

    public void Process(MissionArrowPacket packet) =>
        _playerStats.SetMissionArrow(packet.X, packet.Y);
}
