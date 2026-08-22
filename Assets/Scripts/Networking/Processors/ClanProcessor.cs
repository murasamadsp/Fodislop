#nullable enable

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
        private readonly IPlayerStats _stats;

        public ClanProcessor(IPlayerStats stats)
        {
            _stats = stats;
        }

        public void Process(ShowClanPacket packet)
        {
            var stats = _stats;
            stats?.SetClanId(packet.ClanId);
            var player = PlayerMovementController.LocalPlayer;
            if (player != null && player.TryGetComponent<Robot>(out var robot))
            {
                robot.SetClanBadge(packet.ClanId);
            }
        }

        public void Process(HideClanPacket packet)
        {
            var stats = _stats;
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
