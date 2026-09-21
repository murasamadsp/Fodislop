#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Лежат ли данные окна в памяти — и если нет, заказать их.
/// </summary>
///
/// Окно нельзя собирать по частично приехавшим чанкам: в кадре это выглядит
/// как дыры, которые потом молча зарастают. Поэтому решение принимается до
/// сборки и по всему окну сразу, с каймой в одну клетку (её читают маски
/// соседства).
public static class TerrainResidencyProbe
{
    public static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        Vector2Int gridPosition,
        int width,
        int height)
    {
        if (storage?.CellLayer is not { } layer || mapData == null)
        {
            return false;
        }

        int worldWidth = mapData.WorldWidth;
        int worldHeight = mapData.WorldHeight;
        int minX = Mathf.Max(0, gridPosition.x - 1);
        int maxX = Mathf.Min(worldWidth - 1, gridPosition.x + width);
        int unityMinY = Mathf.Max(0, gridPosition.y - 1);
        int unityMaxY = Mathf.Min(worldHeight - 1, gridPosition.y + height);
        if (minX > maxX || unityMinY > unityMaxY)
        {
            return true;
        }

        int serverMinY = CoordinateUtils.UnityToServerY(unityMaxY, worldHeight);
        int serverMaxY = CoordinateUtils.UnityToServerY(unityMinY, worldHeight);

        int chunkSize = layer.ChunkSize;
        int firstChunkX = minX / chunkSize;
        int lastChunkX = maxX / chunkSize;
        int firstChunkY = serverMinY / chunkSize;
        int lastChunkY = serverMaxY / chunkSize;
        bool resident = true;
        bool missing = false;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                // Request cold disk chunks as well: TryGetCell only probes
                // RAM and can wait forever without initiating a load.
                ChunkReadResult<CellType> result = layer.ReadChunk(
                    chunkY + (chunkX * layer.HeightChunks), touchLru: true);
                resident &= result.Status == ChunkReadStatus.Available;
                missing |= result.Status == ChunkReadStatus.Missing;
            }
        }

        if (missing && connectionService is IWorldRegionRequester requester)
        {
            requester.RequestWorldRegion(
                storage.GetWorldCodeName(),
                new RectInt(minX, serverMinY, maxX - minX + 1, serverMaxY - serverMinY + 1));
        }

        return resident;
    }
}
