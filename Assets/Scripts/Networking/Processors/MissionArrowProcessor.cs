#nullable enable

using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Mission;

namespace Fodinae.Networking.Processors
{
    public class MissionArrowProcessor : IPacketProcessor<MissionArrowPacket>
    {
        private readonly ISessionContainer _session;

        public MissionArrowProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(MissionArrowPacket packet)
        {
            (_session.TryResolve<IPlayerStats>() as PlayerStatsModel)?.SetMissionArrow(packet.X, packet.Y);
        }
    }
}
