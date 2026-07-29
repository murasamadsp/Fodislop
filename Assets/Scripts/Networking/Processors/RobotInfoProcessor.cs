#nullable enable

using Fodinae.Core;
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
            if (Fodinae.Core.ServiceLocator.Resolve<RobotManager>() != null)
            {
                Fodinae.Core.ServiceLocator.Resolve<RobotManager>().UpdateRobotMetadata(packet.BotId, packet.PlayerId, packet.ClanId, packet.Name, packet.Skin, packet.Tail);
            }
        }
    }
}
