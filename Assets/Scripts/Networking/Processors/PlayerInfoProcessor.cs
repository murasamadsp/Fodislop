using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class PlayerInfoProcessor : IPacketProcessor<PlayerInfoPacket>, IPacketProcessor<MovementSpeedPacket>, IPacketProcessor<TeleportPacket>
    {
        public void Process(PlayerInfoPacket packet)
        {
            var rm = (Fodinae.Scripts.Core.ServiceLocator.Resolve<RobotManager>());
            if (rm != null)
            {
                rm.LocalPlayerBotId = packet.BotId;
            }

            var s = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (s != null)
            {
                s.SetNickname(packet.Nickname);
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                if (player.TryGetComponent<Robot>(out var robot))
                {
                    robot.Initialize(packet.BotId);
                }

                player.Initialize(packet.BotId);
            }
        }

        public void Process(MovementSpeedPacket packet)
        {
            var map = Fodinae.Scripts.Core.ServiceLocator.Resolve<IMapDataProvider>();
            map?.UpdateMovementSpeeds(packet);
        }

        public void Process(TeleportPacket packet)
        {
            var player = PlayerMovementController.LocalPlayer ?? Object.FindAnyObjectByType<PlayerMovementController>();
            if (player == null)
            {
                return;
            }

            player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
            player.ResetDirection();
        }
    }
}
