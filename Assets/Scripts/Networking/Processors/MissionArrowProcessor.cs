using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Mission;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class MissionArrowProcessor : IPacketProcessor<MissionArrowPacket>
    {
        public void Process(MissionArrowPacket packet)
        {
            Debug.Log($"[MissionArrowProcessor] Processing MissionArrowPacket: X={packet.X}, Y={packet.Y}");
            (Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>() as PlayerStatsModel)?.SetMissionArrow(packet.X, packet.Y);
        }
    }
}
