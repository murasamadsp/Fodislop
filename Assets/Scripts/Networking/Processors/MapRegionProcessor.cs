#nullable enable

using System;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class MapRegionProcessor : IPacketProcessor<MapRegionPacket>
    {
        private readonly ISessionContainer _session;

        public MapRegionProcessor(ISessionContainer session)
        {
            _session = session;
        }

        public void Process(MapRegionPacket packet)
        {
            MapStorage storage = _session.TryResolve<MapStorage>() ??
                throw new InvalidOperationException(
                    "[MapRegionProcessor] MapStorage is not registered while processing a map region.");

            if (!storage.IsReady || storage.CellLayer == null)
            {
                throw new InvalidOperationException(
                    $"[MapRegionProcessor] MapStorage is not ready for region " +
                    $"({packet.X},{packet.Y}) {packet.Width + 1}x{packet.Height + 1}.");
            }

            if (packet.Payload == null)
            {
                throw new InvalidOperationException(
                    $"[MapRegionProcessor] Map region ({packet.X},{packet.Y}) has null payload.");
            }

            int width = packet.Width + 1;
            int height = packet.Height + 1;
            long expectedCellCount = (long)width * height;
            if (width <= 0 || height <= 0 || packet.Payload.Length < expectedCellCount)
            {
                throw new InvalidOperationException(
                    $"[MapRegionProcessor] Invalid region ({packet.X},{packet.Y}) " +
                    $"{width}x{height}: payload has {packet.Payload.Length} cells, " +
                    $"expected at least {expectedCellCount}.");
            }

            storage.SetRegion(
                packet.X,
                packet.Y,
                width,
                height,
                packet.Payload);
        }
    }
}
