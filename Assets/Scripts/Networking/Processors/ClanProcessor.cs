#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class ClanProcessor : IPacketProcessor<ShowClanPacket>, IPacketProcessor<HideClanPacket>
    {
        public void Process(ShowClanPacket packet)
        {
            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.SetClanId(packet.ClanId);
            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.SetClanBadge(packet.ClanId);
            }
        }

        public void Process(HideClanPacket packet)
        {
            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
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
