#nullable enable

using System;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    /// <summary>
    /// Player/robot identity and state packets: local player identity, robot
    /// metadata, authoritative robot positions and the local player's own
    /// server-authoritative state (speed, teleport, auto-dig, aggression).
    /// </summary>
    public class PlayerInfoProcessor :
        IPacketProcessor<PlayerInfoPacket>,
        IPacketProcessor<MovementSpeedPacket>,
        IPacketProcessor<TeleportPacket>,
        IPacketProcessor<RobotInfoPacket>,
        IPacketProcessor<RobotPositionPacket>,
        IPacketProcessor<AutoMineStatePacket>,
        IPacketProcessor<AggressionStatePacket>
    {
        private readonly IRobotService _robotManager;
        private readonly IPlayerStats _playerStats;
        private readonly IMapDataProvider _mapDataProvider;
        private readonly ILocalPlayerState _localPlayer;

        public PlayerInfoProcessor(
            IRobotService robotManager,
            IPlayerStats playerStats,
            IMapDataProvider mapDataProvider,
            ILocalPlayerState localPlayer)
        {
            _robotManager = robotManager;
            _playerStats = playerStats;
            _mapDataProvider = mapDataProvider;
            _localPlayer = localPlayer;
        }

        public void Process(PlayerInfoPacket packet)
        {
            _robotManager.SetLocalPlayerBotId(packet.BotId);
            _playerStats.SetNickname(packet.Nickname);

            var player = _localPlayer.Current;
            if (player != null)
            {
                if (player.TryGetComponent<IRobotView>(out var robot))
                {
                    robot.Initialize(packet.BotId);
                }

                player.Initialize(packet.BotId);
            }
        }

        public void Process(MovementSpeedPacket packet)
        {
            _mapDataProvider.UpdateMovementSpeeds(packet);
        }

        public void Process(TeleportPacket packet)
        {
            UnityEngine.Debug.Log($"[Probe] Teleport {UnityEngine.Time.realtimeSinceStartup:F3}");
            var player = _localPlayer.Current;
            if (player == null)
            {
                throw new InvalidOperationException("[PlayerInfoProcessor] Teleport received before local player was spawned");
            }

            player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
            player.ResetDirection();
        }

        public void Process(RobotInfoPacket packet)
        {
            var metadata = new RobotMetadata(
                packet.PlayerId,
                packet.ClanId,
                packet.Name,
                packet.Skin,
                packet.Tail);
            _robotManager.UpdateRobotMetadata(packet.BotId, metadata);
        }

        public void Process(RobotPositionPacket packet)
        {
            _robotManager.UpdateRobotPosition(packet.BotId, packet.X, packet.Y, packet.Rotation);
            if (packet.BotId != 0 && packet.BotId == _robotManager.LocalPlayerBotId)
            {
                _localPlayer.Current?.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
            }
        }

        public void Process(AutoMineStatePacket packet)
        {
            var player = _localPlayer.Current;
            if (player != null)
            {
                player.AutoDig = packet.Enabled;
            }
        }

        public void Process(AggressionStatePacket packet)
        {
            var player = _localPlayer.Current;
            if (player != null)
            {
                player.Aggression = packet.Enabled;
            }
        }
    }
}
