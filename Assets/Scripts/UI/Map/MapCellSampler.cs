#nullable enable

using System.Collections.Generic;
using Fodinae.World;
using MinesServer.Data;

namespace Fodinae.UI
{
    /// <summary>
    /// Shared non-allocating chunk sampler for the minimap and fullscreen map.
    /// A missing chunk starts its normal WorldLayer async load and is never
    /// confused with a real CellType value.
    /// </summary>
    internal sealed class MapCellSampler
    {
        private const int MaxChunkCacheEntries = 4096;

        private readonly Dictionary<int, CellType[]?> _chunks = new();
        private readonly Queue<int> _chunkOrder = new();
        private IWorldLayer<CellType>? _layer;
        private int _chunkSize;
        private int _heightChunks;

        private int _lastChunkIndex = -1;
        private CellType[]? _lastChunk;

        public void Bind(IWorldLayer<CellType>? layer)
        {
            if (ReferenceEquals(_layer, layer))
            {
                return;
            }

            _layer = layer;
            _chunks.Clear();
            _chunkOrder.Clear();
            _lastChunkIndex = -1;
            _lastChunk = null;
            _chunkSize = layer?.ChunkSize ?? 0;
            _heightChunks = layer?.HeightChunks ?? 0;
        }

        public void Invalidate()
        {
            _chunks.Clear();
            _chunkOrder.Clear();
            _lastChunkIndex = -1;
            _lastChunk = null;
        }

        public void InvalidateChunk(int serverX, int serverY)
        {
            if (_layer == null || _chunkSize <= 0 || _heightChunks <= 0)
            {
                return;
            }

            int chunkX = serverX / _chunkSize;
            int chunkY = serverY / _chunkSize;
            int chunkIndex = chunkY + (chunkX * _heightChunks);
            _chunks.Remove(chunkIndex);
            if (_lastChunkIndex == chunkIndex)
            {
                _lastChunkIndex = -1;
                _lastChunk = null;
            }
        }

        public bool TryGetCell(int serverX, int serverY, out CellType cellType)
        {
            cellType = CellType.Unloaded;
            if (_layer == null || _chunkSize <= 0 || _heightChunks <= 0 ||
                serverX < 0 || serverY < 0 ||
                serverX >= _layer.WidthChunks * _chunkSize ||
                serverY >= _layer.HeightChunks * _chunkSize)
            {
                return false;
            }

            int chunkX = serverX / _chunkSize;
            int chunkY = serverY / _chunkSize;
            int chunkIndex = chunkY + (chunkX * _heightChunks);

            CellType[]? chunk;
            if (chunkIndex == _lastChunkIndex)
            {
                chunk = _lastChunk;
            }
            else
            {
                if (!_chunks.TryGetValue(chunkIndex, out chunk))
                {
                    ChunkReadResult<CellType> result = _layer.ReadChunk(chunkIndex, touchLru: false);
                    if (result.Status == ChunkReadStatus.Available)
                    {
                        chunk = result.Data;
                        _chunks[chunkIndex] = chunk;
                        _chunkOrder.Enqueue(chunkIndex);
                        TrimCache();
                    }
                }

                _lastChunkIndex = chunkIndex;
                _lastChunk = chunk;
            }

            if (chunk == null)
            {
                return false;
            }

            int localX = serverX % _chunkSize;
            int localY = serverY % _chunkSize;
            cellType = chunk[localY + (localX * _chunkSize)];
            return true;
        }

        private void TrimCache()
        {
            while (_chunks.Count > MaxChunkCacheEntries && _chunkOrder.Count > 0)
            {
                _chunks.Remove(_chunkOrder.Dequeue());
            }
        }
    }
}
