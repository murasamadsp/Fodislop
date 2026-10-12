#nullable enable

using System;
using System.Collections.Generic;
using Kern;
using Kern.UI;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapCellSamplerTests
{
    [Test]
    public void InvalidateChunk_RemovesItsOldCachePositionBeforeReload()
    {
        var layer = new CountingLayer(widthChunks: 4097);
        var sampler = new MapCellSampler();
        sampler.Bind(layer);

        Assert.That(sampler.TryGetChunk(0, 0, out _), Is.True);
        Assert.That(sampler.TryGetChunk(1, 0, out _), Is.True);
        sampler.InvalidateChunk(0, 0);
        Assert.That(sampler.TryGetChunk(0, 0, out _), Is.True);

        for (int chunkX = 2; chunkX <= 4096; chunkX++)
        {
            Assert.That(sampler.TryGetChunk(chunkX, 0, out _), Is.True);
        }

        Assert.That(layer.ReadCounts[0], Is.EqualTo(2));
        Assert.That(sampler.TryGetChunk(0, 0, out _), Is.True);
        Assert.That(layer.ReadCounts[0], Is.EqualTo(2), "The reloaded chunk should remain newer than chunk 1 in the eviction order.");
    }

    private sealed class CountingLayer(int widthChunks) : IWorldLayer<CellType>
    {
        public Dictionary<int, int> ReadCounts { get; } = new();

        public event Action<int, int, int, int>? ChunkLoaded
        {
            add { }
            remove { }
        }

        public int ChunkSize => 1;
        public int WidthChunks => widthChunks;
        public int HeightChunks => 1;
        public int MaxChunksInMemory => int.MaxValue;
        public bool HasDirtyChunks => false;
        public CellType this[int x, int y] { get => CellType.Empty; set => throw new NotSupportedException(); }
        public void NotifyRegionLoaded(int startX, int startY, int width, int height) => throw new NotSupportedException();
        public IEnumerable<int> GetLoadedChunkIndices() => Array.Empty<int>();
        public int GetLoadedCount() => 0;
        public int GetDirtyCount() => 0;
        public CellType GetCell(int x, int y, bool touchLRU = true) => throw new NotSupportedException();
        public CellType GetCellSync(int x, int y, bool touchLRU = true) => throw new NotSupportedException();
        public bool TryGetCell(int x, int y, out CellType value) { value = CellType.Empty; return true; }
        public void SetCell(int x, int y, CellType value) => throw new NotSupportedException();
        public int SetRegion(int startX, int startY, int width, int height, CellType[] cells, int cellsOffset = 0) => throw new NotSupportedException();
        public int SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells, int cellsOffset = 0) => throw new NotSupportedException();
        public CellType[] GetOrCreateChunk(int chunkIndex, bool touchLRU = true) => throw new NotSupportedException();

        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLRU = true)
        {
            ReadCounts.TryGetValue(chunkIndex, out int count);
            ReadCounts[chunkIndex] = count + 1;
            return new ChunkReadResult<CellType>(ChunkReadStatus.Available, new[] { CellType.Empty }, null);
        }

        public void Flush(bool flushToDisk = false) => throw new NotSupportedException();
        public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex) { chunkIndex = x; localIndex = 0; return (uint)x < widthChunks && y == 0; }
        public void Dispose() { }
    }
}
