#nullable enable

using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Networking.Processors
{
    public class MissionProcessor : IPacketProcessor<MissionInitPacket>, IPacketProcessor<MissionProgressPacket>
    {
        private readonly ISessionContainer _session;

        public MissionProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(MissionInitPacket packet)
        {
            var s = _session.TryResolve<IPlayerStats>();
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
            var s = _session.TryResolve<IPlayerStats>();
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
