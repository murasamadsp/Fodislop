#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Networking.Processors
{
    public class MissionArrowProcessor : IPacketProcessor<MissionArrowPacket>
    {
        private readonly IPlayerStats _playerStats;

        public MissionArrowProcessor(IPlayerStats playerStats)
        {
            _playerStats = playerStats;
        }

        public void Process(MissionArrowPacket packet)
        {
            _playerStats.SetMissionArrow(packet.X, packet.Y);
        }
    }
}
