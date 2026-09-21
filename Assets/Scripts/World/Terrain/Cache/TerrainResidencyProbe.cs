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
    /// <summary>
    /// Лежит ли окно в памяти. Ничего не заказывает — этим можно щупать
    /// промежуточные положения окна, не поднимая сетевого трафика.
    /// </summary>
    public static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        Vector2Int gridPosition,
        int width,
        int height) =>
        Probe(storage, mapData, null, gridPosition, width, height);

    /// <summary>
    /// То же самое, но недостающие чанки заказываются у сервера.
    /// </summary>
    public static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        Vector2Int gridPosition,
        int width,
        int height) =>
        Probe(storage, mapData, connectionService, gridPosition, width, height);

    private static bool Probe(
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
            // Заказ выравнивается по границам чанков и берётся с запасом в
            // чанк во все стороны. Причина — не запас как таковой, а
            // УСТОЙЧИВОСТЬ прямоугольника: пока он совпадает с уже заказанным,
            // повторный запрос не нужен. Точный по окну прямоугольник менялся
            // на каждом шаге камеры, каждый шаг порождал новый запрос, а новый
            // запрос отменяет предыдущий — поток чанков рвался ровно тогда,
            // когда игрок шёл.
            int requestMinX = Mathf.Max(0, ((minX / chunkSize) - 1) * chunkSize);
            int requestMinY = Mathf.Max(0, ((serverMinY / chunkSize) - 1) * chunkSize);
            int requestMaxX = Mathf.Min(worldWidth - 1, (((maxX / chunkSize) + 2) * chunkSize) - 1);
            int requestMaxY = Mathf.Min(worldHeight - 1, (((serverMaxY / chunkSize) + 2) * chunkSize) - 1);
            requester.RequestWorldRegion(
                storage.GetWorldCodeName(),
                new RectInt(
                    requestMinX,
                    requestMinY,
                    requestMaxX - requestMinX + 1,
                    requestMaxY - requestMinY + 1));
        }

        return resident;
    }
}
