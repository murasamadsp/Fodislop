#nullable enable

using Fodinae;
using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyMapStreamer
{
    private static readonly Dictionary<int, CellType[]> _chunkPayloadCache = new();
    private static readonly object _cacheLock = new object();

    public static void SendMapChunksAround(IWorldLayer<CellType>? worldLayer, HashSet<int> sentMapChunks, ushort serverX, ushort serverY, Action<ServerPacket> sendPacket)
    {
        const int ChunkSize = 32;
        const int StreamingRadiusChunks = 4;
        if (worldLayer == null)
        {
            throw new InvalidOperationException(
                "Cannot stream map chunks before the DummyConnection world layer is initialized.");
        }

        int centerChunkX = serverX / ChunkSize;
        int centerChunkY = serverY / ChunkSize;
        int minimumChunkX = Math.Max(0, centerChunkX - StreamingRadiusChunks);
        int maximumChunkX = Math.Min(
            worldLayer.WidthChunks - 1,
            centerChunkX + StreamingRadiusChunks);
        int minimumChunkY = Math.Max(0, centerChunkY - StreamingRadiusChunks);
        int maximumChunkY = Math.Min(
            worldLayer.HeightChunks - 1,
            centerChunkY + StreamingRadiusChunks);
        for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
        {
            for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
            {
                int chunkIndex = chunkY + (chunkX * worldLayer.HeightChunks);
                if (sentMapChunks.Contains(chunkIndex))
                {
                    continue;
                }

                CellType[] source = worldLayer.GetOrCreateChunk(chunkIndex, touchLru: true);

                CellType[] payload = GetOrCreatePayload(chunkIndex, source);
                sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new MapRegionPacket(
                        (ushort)(chunkX * ChunkSize),
                        (ushort)(chunkY * ChunkSize),
                        ChunkSize - 1,
                        ChunkSize - 1,
                        payload),
                })));

                sentMapChunks.Add(chunkIndex);
            }
        }
    }

    private static CellType[] GetOrCreatePayload(int chunkIndex, CellType[] source)
    {
        lock (_cacheLock)
        {
            if (_chunkPayloadCache.TryGetValue(chunkIndex, out CellType[]? cached))
            {
                return cached;
            }
        }

        const int ChunkSize = 32;
        var payload = new CellType[source.Length];
        for (int lx = 0; lx < ChunkSize; lx++)
        {
            for (int ly = 0; ly < ChunkSize; ly++)
            {
                payload[(ly * ChunkSize) + lx] = source[ly + (lx * ChunkSize)];
            }
        }

        lock (_cacheLock)
        {
            _chunkPayloadCache[chunkIndex] = payload;
        }

        return payload;
    }
}
