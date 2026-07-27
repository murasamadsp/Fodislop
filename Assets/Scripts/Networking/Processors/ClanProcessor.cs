using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class ClanProcessor : IPacketProcessor<ShowClanPacket>, IPacketProcessor<HideClanPacket>
    {
        public void Process(ShowClanPacket packet)
        {
            var stats = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.SetClanId(packet.ClanId);
            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.SetClanBadge(packet.ClanId);
            }
        }

        public void Process(HideClanPacket packet)
        {
            var stats = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (stats != null)
            {
                stats.SetClanId(0);
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.ClearClanBadge();
            }
        }
    }
}
