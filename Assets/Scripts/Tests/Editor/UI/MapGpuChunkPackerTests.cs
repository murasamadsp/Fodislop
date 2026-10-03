#nullable enable

using Kern.UI;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapGpuChunkPackerTests
{
    [TestCase(0, 0, 0, 0, 0, 0)]
    [TestCase(1, 0, 0, 0, 1, 0)]
    [TestCase(0, 1, 0, 0, 0, 1)]
    [TestCase(1, 1, 0, 0, 1, 1)]
    [TestCase(2, 3, 1, 1, 0, 1)]
    [TestCase(3, 2, 1, 1, 1, 0)]
    public void GetStorageChunkAddress_MapsSixteenCellGridIntoThirtyTwoCellChunks(
        int gpuChunkX,
        int gpuChunkY,
        int expectedStorageChunkX,
        int expectedStorageChunkY,
        int expectedSubChunkX,
        int expectedSubChunkY)
    {
        int subChunksPerStorageChunk = MapGpuChunkPacker.GetSubChunksPerStorageChunk(32);

        MapGpuChunkPacker.GetStorageChunkAddress(
            gpuChunkX,
            gpuChunkY,
            subChunksPerStorageChunk,
            out int storageChunkX,
            out int storageChunkY,
            out int subChunkX,
            out int subChunkY);

        Assert.That(storageChunkX, Is.EqualTo(expectedStorageChunkX));
        Assert.That(storageChunkY, Is.EqualTo(expectedStorageChunkY));
        Assert.That(subChunkX, Is.EqualTo(expectedSubChunkX));
        Assert.That(subChunkY, Is.EqualTo(expectedSubChunkY));
    }

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    public void PackSubChunk_PreservesEveryCellAndGPUColumnMajorAddress(int subChunkX, int subChunkY)
    {
        const int StorageChunkSize = 32;
        const int GpuChunkSize = MapGpuChunkPacker.GpuChunkSize;
        const int ChunkSlot = 2;
        var storageChunk = new CellType[StorageChunkSize * StorageChunkSize];
        var packed = new uint[MapGpuChunkPacker.PackedUIntCount * 3];
        for (int x = 0; x < StorageChunkSize; x++)
        {
            for (int y = 0; y < StorageChunkSize; y++)
            {
                storageChunk[x * StorageChunkSize + y] = (CellType)((x * 29 + y * 11 + 1) & 0xff);
            }
        }

        MapGpuChunkPacker.PackSubChunk(
            storageChunk,
            StorageChunkSize,
            subChunkX,
            subChunkY,
            packed,
            ChunkSlot);

        for (int localX = 0; localX < GpuChunkSize; localX++)
        {
            for (int localY = 0; localY < GpuChunkSize; localY++)
            {
                int gpuCellIndex = localY + localX * GpuChunkSize;
                uint packedWord = packed[ChunkSlot * MapGpuChunkPacker.PackedUIntCount + (gpuCellIndex >> 2)];
                byte actual = (byte)(packedWord >> ((gpuCellIndex & 3) * 8));
                int sourceX = subChunkX * GpuChunkSize + localX;
                int sourceY = subChunkY * GpuChunkSize + localY;
                byte expected = (byte)storageChunk[sourceX * StorageChunkSize + sourceY];
                Assert.That(actual, Is.EqualTo(expected), $"Cell mismatch at subchunk {subChunkX},{subChunkY}, local {localX},{localY}.");
            }
        }
    }

    [Test]
    public void GetSubChunksPerStorageChunk_RejectsMisalignedStorageChunkSize()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => MapGpuChunkPacker.GetSubChunksPerStorageChunk(24));
    }
}
