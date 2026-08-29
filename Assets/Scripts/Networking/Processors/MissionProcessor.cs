#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Networking.Processors
{
    public class MissionProcessor : IPacketProcessor<MissionInitPacket>, IPacketProcessor<MissionProgressPacket>
    {
        private readonly IPlayerStats _playerStats;

        public MissionProcessor(IPlayerStats playerStats)
        {
            _playerStats = playerStats;
        }

        public void Process(MissionInitPacket packet)
        {
            if (string.IsNullOrEmpty(packet.Title))
            {
                _playerStats.ClearMission();
                return;
            }

            _playerStats.SetMission(packet.Title, packet.Description, 0);
        }

        public void Process(MissionProgressPacket packet)
        {
            _playerStats.SetMissionProgress(packet.Current);
            if (packet.Max > 0)
            {
                _playerStats.SetMissionMaxProgress(packet.Max);
            }
        }
    }
}
