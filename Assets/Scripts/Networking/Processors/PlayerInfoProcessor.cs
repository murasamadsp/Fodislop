#nullable enable

using System;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class PlayerInfoProcessor : IPacketProcessor<PlayerInfoPacket>, IPacketProcessor<MovementSpeedPacket>, IPacketProcessor<TeleportPacket>
    {
        private readonly ISessionContainer _session;

        public PlayerInfoProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(PlayerInfoPacket packet)
        {
            var rm = _session.TryResolve<RobotManager>();
            if (rm != null)
            {
                rm.SetLocalPlayerBotId(packet.BotId);
            }

            var s = _session.TryResolve<IPlayerStats>();
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
            var map = _session.TryResolve<IMapDataProvider>();
            map?.UpdateMovementSpeeds(packet);
        }

        public void Process(TeleportPacket packet)
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("[PlayerInfoProcessor] Teleport received before local player was spawned");
            }

            player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
            player.ResetDirection();
        }
    }
}
