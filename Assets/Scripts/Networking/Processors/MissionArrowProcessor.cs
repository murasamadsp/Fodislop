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
            PlayerStatsModel.Instance?.SetMissionArrow(packet.X, packet.Y);
        }
    }
}
