#nullable enable

using System;
using Fodinae.Core.DI;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
    {
        private readonly ISessionContainer _session;

        public WorldInitProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(WorldInitPacket packet)
        {
            Debug.Log("[WorldInitProcessor] Processing WorldInitPacket");
            var mm = _session.TryResolve<MapManager>();
            if (mm == null)
            {
                throw new InvalidOperationException(
                    "MapManager is not registered; cannot process WorldInitPacket.");
            }

            mm.LoadWorldInit(packet);
        }
    }
}
