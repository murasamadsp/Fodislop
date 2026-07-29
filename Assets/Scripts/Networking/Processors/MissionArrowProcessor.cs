#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Mission;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class MissionArrowProcessor : IPacketProcessor<MissionArrowPacket>
    {
        public void Process(MissionArrowPacket packet)
        {
            Debug.Log($"[MissionArrowProcessor] Processing MissionArrowPacket: X={packet.X}, Y={packet.Y}");
            (Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>() as PlayerStatsModel)?.SetMissionArrow(packet.X, packet.Y);
        }
    }
}
