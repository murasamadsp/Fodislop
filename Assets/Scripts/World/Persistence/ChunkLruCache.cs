#nullable enable

namespace Fodinae.Persistence;

using System;
using System.Collections.Generic;

/// <summary>
/// In-memory LRU cache for world layer chunks with dirty tracking and eviction notifications.
/// </summary>
/// <typeparam name="T">Unmanaged cell value type.</typeparam>
public sealed class ChunkLruCache<T>
    where T : unmanaged
{
    private readonly int _maxCapacity;
    private readonly Action<int, T[]>? _onEvictDirty;
    private readonly Dictionary<int, T[]> _loadedChunks;
    private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
    private readonly LinkedList<int> _lruList;
    private readonly HashSet<int> _dirtyChunks;

    public ChunkLruCache(int maxCapacity, Action<int, T[]>? onEvictDirty = null)
    {
        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCapacity),
                maxCapacity,
                "Cache capacity must be positive.");
        }

        _maxCapacity = maxCapacity;
        _onEvictDirty = onEvictDirty;
        _loadedChunks = new Dictionary<int, T[]>(maxCapacity);
        _lruIndexMap = new Dictionary<int, LinkedListNode<int>>(maxCapacity);
        _lruList = new LinkedList<int>();
        _dirtyChunks = new HashSet<int>();
    }

    public int Capacity => _maxCapacity;

    public int LoadedCount => _loadedChunks.Count;

    public int DirtyCount => _dirtyChunks.Count;

    public bool HasDirtyChunks => _dirtyChunks.Count > 0;

    public IEnumerable<int> LoadedIndices => _loadedChunks.Keys;

    public IEnumerable<int> DirtyIndices => _dirtyChunks;

    public bool Contains(int chunkIndex) => _loadedChunks.ContainsKey(chunkIndex);

    public bool IsDirty(int chunkIndex) => _dirtyChunks.Contains(chunkIndex);

    public bool TryGet(int chunkIndex, out T[]? chunk)
    {
        return _loadedChunks.TryGetValue(chunkIndex, out chunk);
    }

    public void Touch(int chunkIndex)
    {
        if (_lruIndexMap.TryGetValue(chunkIndex, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);
        }
    }

    public void AddOrUpdate(int chunkIndex, T[] chunk)
    {
        if (chunk == null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        if (_lruIndexMap.TryGetValue(chunkIndex, out var existingNode))
        {
            _lruList.Remove(existingNode);
            _lruIndexMap.Remove(chunkIndex);
            _loadedChunks.Remove(chunkIndex);
        }

        if (_loadedChunks.Count >= _maxCapacity)
        {
            EvictOldest();
        }

        _loadedChunks[chunkIndex] = chunk;
        var node = _lruList.AddFirst(chunkIndex);
        _lruIndexMap[chunkIndex] = node;
    }

    public void MarkDirty(int chunkIndex)
    {
        _dirtyChunks.Add(chunkIndex);
    }

    public void ClearDirty()
    {
        _dirtyChunks.Clear();
    }

    public void Clear()
    {
        _loadedChunks.Clear();
        _lruIndexMap.Clear();
        _lruList.Clear();
        _dirtyChunks.Clear();
    }

    private void EvictOldest()
    {
        if (_lruList.Count == 0 || _lruList.Last == null)
        {
            return;
        }

        int oldestIndex = _lruList.Last.Value;
        if (_dirtyChunks.Contains(oldestIndex) &&
            _loadedChunks.TryGetValue(oldestIndex, out T[]? dirtyChunk))
        {
            _onEvictDirty?.Invoke(oldestIndex, dirtyChunk);
            _dirtyChunks.Remove(oldestIndex);
        }

        _loadedChunks.Remove(oldestIndex);
        _lruIndexMap.Remove(oldestIndex);
        _lruList.RemoveLast();
    }
}
