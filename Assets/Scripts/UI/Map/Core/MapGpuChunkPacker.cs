#nullable enable

using System;
using MinesServer.Data;

namespace Kern.UI;

internal static class MapGpuChunkPacker
{
    internal const int GpuChunkSize = 16;
    internal const int PackedUIntCount = GpuChunkSize * GpuChunkSize / sizeof(uint);

    internal static int GetSubChunksPerStorageChunk(int storageChunkSize)
    {
        if (storageChunkSize <= 0 || storageChunkSize % GpuChunkSize != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(storageChunkSize),
                storageChunkSize,
                $"Storage chunk size must be a positive multiple of the GPU chunk size ({GpuChunkSize}).");
        }

        return storageChunkSize / GpuChunkSize;
    }

    internal static void GetStorageChunkAddress(
        int gpuChunkX,
        int gpuChunkY,
        int subChunksPerStorageChunk,
        out int storageChunkX,
        out int storageChunkY,
        out int subChunkX,
        out int subChunkY)
    {
        if (gpuChunkX < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gpuChunkX));
        }

        if (gpuChunkY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gpuChunkY));
        }

        if (subChunksPerStorageChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subChunksPerStorageChunk));
        }

        storageChunkX = gpuChunkX / subChunksPerStorageChunk;
        storageChunkY = gpuChunkY / subChunksPerStorageChunk;
        subChunkX = gpuChunkX % subChunksPerStorageChunk;
        subChunkY = gpuChunkY % subChunksPerStorageChunk;
    }

    internal static void PackSubChunk(
        CellType[] storageChunk,
        int storageChunkSize,
        int subChunkX,
        int subChunkY,
        uint[] destination,
        int chunkSlot)
    {
        if (storageChunk == null)
        {
            throw new ArgumentNullException(nameof(storageChunk));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        int subChunksPerStorageChunk = GetSubChunksPerStorageChunk(storageChunkSize);
        int requiredCells = checked(storageChunkSize * storageChunkSize);
        if (storageChunk.Length < requiredCells)
        {
            throw new ArgumentException("Storage chunk is smaller than its declared dimensions.", nameof(storageChunk));
        }

        if (subChunkX < 0 || subChunkX >= subChunksPerStorageChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(subChunkX));
        }

        if (subChunkY < 0 || subChunkY >= subChunksPerStorageChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(subChunkY));
        }

        if (chunkSlot < 0 || chunkSlot > destination.Length / PackedUIntCount - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSlot));
        }

        int baseOffset = chunkSlot * PackedUIntCount;
        Array.Clear(destination, baseOffset, PackedUIntCount);
        int sourceStartX = subChunkX * GpuChunkSize;
        int sourceStartY = subChunkY * GpuChunkSize;
        for (int localX = 0; localX < GpuChunkSize; localX++)
        {
            int sourceColumnOffset = (sourceStartX + localX) * storageChunkSize + sourceStartY;
            int gpuCellOffset = localX * GpuChunkSize;
            for (int localY = 0; localY < GpuChunkSize; localY++)
            {
                int gpuCellIndex = gpuCellOffset + localY;
                uint cellType = (byte)storageChunk[sourceColumnOffset + localY];
                destination[baseOffset + (gpuCellIndex >> 2)] |=
                    cellType << ((gpuCellIndex & 3) << 3);
            }
        }
    }
}
