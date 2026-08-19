#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    /// <summary>
    /// Decoupled SOLID Processor for Robot Metadata & Position Info Packets.
    /// Updates RobotManager metadata state and robot visual components.
    /// </summary>
    public class RobotInfoProcessor : IPacketProcessor<RobotInfoPacket>
    {
        public void Process(RobotInfoPacket packet)
        {
            var mgr = ServiceLocator.Resolve<IRobotService>();
            var metadata = new RobotMetadata(
                packet.PlayerId,
                packet.ClanId,
                packet.Name,
                packet.Skin,
                packet.Tail);
            mgr?.UpdateRobotMetadata(packet.BotId, metadata);
        }
    }
}
