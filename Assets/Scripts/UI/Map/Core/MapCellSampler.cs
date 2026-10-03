#nullable enable

using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;

namespace Kern.UI;
internal sealed class MapCellSampler
{
    private const int MaxChunkCacheEntries = 4096;

    private readonly Dictionary<int, CellType[]?> _chunks = new();
    private readonly LinkedList<int> _chunkOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _chunkOrderNodes = new();
    private readonly Stack<LinkedListNode<int>> _freeChunkOrderNodes = new();
    private IWorldLayer<CellType>? _layer;
    private int _chunkSize;
    private int _heightChunks;

    public IWorldLayer<CellType>? Layer => _layer;

    public int ChunkSize => _chunkSize;

    public int WidthChunks => _layer?.WidthChunks ?? 0;

    public int HeightChunks => _heightChunks;

    public int Revision { get; private set; }

    // Границы мира в клетках кешируются: попиксельный опрос карты обращается к
    // слою миллионы раз за кадр, и чтение свойств через IWorldLayer на каждом
    // пикселе стоило дороже всей остальной выборки.
    private int _worldWidth;
    private int _worldHeight;

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
        _chunkOrderNodes.Clear();
        _freeChunkOrderNodes.Clear();
        _lastChunkIndex = -1;
        _lastChunk = null;
        _chunkSize = layer?.ChunkSize ?? 0;
        _heightChunks = layer?.HeightChunks ?? 0;
        Revision++;
        _worldWidth = _chunkSize * (layer?.WidthChunks ?? 0);
        _worldHeight = _chunkSize * _heightChunks;
    }

    public void Invalidate()
    {
        _chunks.Clear();
        _chunkOrder.Clear();
        _chunkOrderNodes.Clear();
        _freeChunkOrderNodes.Clear();
        _lastChunkIndex = -1;
        _lastChunk = null;
        Revision++;
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
        if (_chunkOrderNodes.TryGetValue(chunkIndex, out LinkedListNode<int>? node))
        {
            _chunkOrderNodes.Remove(chunkIndex);
            _chunkOrder.Remove(node);
            _freeChunkOrderNodes.Push(node);
        }

        if (_lastChunkIndex == chunkIndex)
        {
            _lastChunkIndex = -1;
            _lastChunk = null;
        }

        Revision++;
    }

    public bool TryGetChunk(int chunkX, int chunkY, out CellType[]? chunk)
    {
        chunk = null;
        if (_layer == null || _chunkSize <= 0 || _heightChunks <= 0 ||
            chunkX < 0 || chunkY < 0 ||
            chunkX >= _layer.WidthChunks || chunkY >= _heightChunks)
        {
            return false;
        }

        int chunkIndex = chunkY + (chunkX * _heightChunks);
        if (chunkIndex == _lastChunkIndex)
        {
            chunk = _lastChunk;
            return chunk != null;
        }

        if (_chunks.TryGetValue(chunkIndex, out chunk))
        {
            _lastChunkIndex = chunkIndex;
            _lastChunk = chunk;
            return chunk != null;
        }

        ChunkReadResult<CellType> result = _layer.ReadChunk(chunkIndex, touchLRU: false);
        chunk = (result.Status == ChunkReadStatus.Available) ? result.Data : null;
        _chunks[chunkIndex] = chunk;
        LinkedListNode<int> node = _freeChunkOrderNodes.Count > 0
            ? _freeChunkOrderNodes.Pop()
            : new LinkedListNode<int>(chunkIndex);
        node.Value = chunkIndex;
        _chunkOrder.AddLast(node);
        _chunkOrderNodes.Add(chunkIndex, node);
        TrimCache();
        _lastChunkIndex = chunkIndex;
        _lastChunk = chunk;
        return chunk != null;
    }

    public bool TryGetCell(int serverX, int serverY, out CellType cellType)
    {
        cellType = CellType.Unloaded;
        if (_layer == null || _chunkSize <= 0 || _heightChunks <= 0 ||
            serverX < 0 || serverY < 0 ||
            serverX >= _worldWidth ||
            serverY >= _worldHeight)
        {
            return false;
        }

        int chunkX = serverX / _chunkSize;
        int chunkY = serverY / _chunkSize;
        if (!TryGetChunk(chunkX, chunkY, out CellType[]? chunk) || chunk == null)
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
            LinkedListNode<int> oldest = _chunkOrder.First!;
            _chunkOrder.RemoveFirst();
            _chunkOrderNodes.Remove(oldest.Value);
            _chunks.Remove(oldest.Value);
            _freeChunkOrderNodes.Push(oldest);
        }
    }
}
