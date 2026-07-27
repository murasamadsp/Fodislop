using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Scripts.Networking.Processors
{
    public class MissionProcessor : IPacketProcessor<MissionInitPacket>, IPacketProcessor<MissionProgressPacket>
    {
        public void Process(MissionInitPacket packet)
        {
            var s = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (s == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(packet.Title))
            {
                s.ClearMission();
                return;
            }

            s.SetMission(packet.Title, packet.Description, 0);
        }

        public void Process(MissionProgressPacket packet)
        {
            var s = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (s == null)
            {
                return;
            }

            s.SetMissionProgress(packet.Current);
            if (packet.Max > 0)
            {
                s.SetMissionMaxProgress(packet.Max);
            }
        }
    }
}
