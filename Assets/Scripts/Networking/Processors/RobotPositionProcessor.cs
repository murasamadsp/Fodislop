#nullable enable

using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class RobotPositionProcessor : IPacketProcessor<RobotPositionPacket>
    {
        private readonly ISessionContainer _session;

        public RobotPositionProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(RobotPositionPacket packet)
        {
            var rm = _session.TryResolve<RobotManager>();
            if (rm == null)
            {
                return;
            }

            rm.UpdateRobotPosition(packet.BotId, packet.X, packet.Y, packet.Rotation);
            if (packet.BotId != 0 && packet.BotId == rm.LocalPlayerBotId)
            {
                var controller = PlayerMovementController.LocalPlayer;
                if (controller != null)
                {
                    controller.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
                }
            }
        }
    }
}
