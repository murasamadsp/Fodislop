#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Fodinae
{
    public class WorldLayer<T> : IDisposable
        where T : unmanaged
    {
        private const int HEADER_SIZE = 16; // 4 ints

        private readonly int _chunkSize;
        private readonly int _chunkArea;
        private readonly int _widthChunks;
        private readonly int _heightChunks;
        private readonly int _maxChunksInMemory;
        private readonly string _filePath;
        private readonly object _ioLock = new object();

        // The Look-Up Table (FAT). Stores file offset for each chunk.
        private readonly long[] _chunkOffsets;

        // --- Memory Cache (LRU) ---
        private readonly Dictionary<int, T[]> _loadedChunks;
        private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
        private readonly LinkedList<int> _lruList;
        private readonly HashSet<int> _dirtyChunks;
        private readonly HashSet<int> _loadingChunks;
        private bool _disposed;

        private FileStream? _fileStream;

        public WorldLayer(string filePath, int WIDTH_CHUNKS, int HEIGHT_CHUNKS, int CHUNK_SIZE = 32, int maxRamChunks = 1000)
        {
            _filePath = filePath;
            _widthChunks = WIDTH_CHUNKS;
            _heightChunks = HEIGHT_CHUNKS;
            _chunkSize = CHUNK_SIZE;
            _chunkArea = CHUNK_SIZE * CHUNK_SIZE;
            _maxChunksInMemory = maxRamChunks;

            int totalChunks = WIDTH_CHUNKS * HEIGHT_CHUNKS;
            _chunkOffsets = new long[totalChunks];
            Array.Fill(_chunkOffsets, -1);

            _loadedChunks = new Dictionary<int, T[]>(maxRamChunks);
            _lruIndexMap = new Dictionary<int, LinkedListNode<int>>(maxRamChunks);
            _lruList = new LinkedList<int>();
            _dirtyChunks = new HashSet<int>();
            _loadingChunks = new HashSet<int>();

            InitializeFile();
        }

        public int ChunkSize => _chunkSize;

        public int WidthChunks => _widthChunks;

        public int HeightChunks => _heightChunks;

        public int MaxChunksInMemory => _maxChunksInMemory;

        public event Action<int, int, int, int>? ChunkLoaded;

        public T this[int x, int y]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GetCell(x, y, touchLru: true);
            set => SetCell(x, y, value);
        }

        // --- Debug Access ---
        public IEnumerable<int> GetLoadedChunkIndices()
        {
            return _loadedChunks.Keys;
        }

        public long[] GetChunkOffsets()
        {
            return _chunkOffsets;
        }

        public int GetLoadedCount()
        {
            return _loadedChunks.Count;
        }

        public int GetDirtyCount()
        {
            return _dirtyChunks.Count;
        }

        public bool HasDirtyChunks => _dirtyChunks.Count > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetCell(int x, int y, bool touchLru = true)
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
            }

            T[]? chunk = GetChunk(chunkIndex, createIfMissing: false, touchLru: touchLru);
            if (chunk == null)
            {
                throw new InvalidDataException(
                    $"World layer '{_filePath}' has no loaded chunk for cell ({x}, {y}).");
            }

            return chunk[localIndex];
        }

        public T GetCellSync(int x, int y, bool touchLru = true)
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
            }

            T[]? chunk = GetChunk(chunkIndex, createIfMissing: true, touchLru: touchLru);
            if (chunk == null)
            {
                throw new InvalidDataException(
                    $"World layer '{_filePath}' could not load chunk for cell ({x}, {y}).");
            }

            return chunk[localIndex];
        }

        public void SetCell(int x, int y, T value)
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
            }

            T[]? chunk = GetChunk(chunkIndex, createIfMissing: true, touchLru: true);

            if (chunk != null && !EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
            {
                chunk[localIndex] = value;
                MarkDirty(chunkIndex);
            }
        }

        // --- Core Paging Logic ---
        public T[]? GetChunk(int chunkIndex, bool createIfMissing = false, bool touchLru = true)
        {
            if (_disposed || chunkIndex < 0 || chunkIndex >= _chunkOffsets.Length)
            {
                return null;
            }

            if (_loadedChunks.TryGetValue(chunkIndex, out T[]? chunk))
            {
                if (touchLru)
                {
                    TouchLru(chunkIndex);
                }

                return chunk;
            }

            if (createIfMissing)
            {
                try
                {
                    chunk = LoadChunkFromDisk(chunkIndex);
                    if (chunk == null)
                    {
                        chunk = new T[_chunkArea];
                    }

                    AddToCache(chunkIndex, chunk);
                    return chunk;
                }
                catch (IOException ioEx)
                {
                    throw new IOException($"[WorldLayer] Could not load/create chunk {chunkIndex}: {ioEx.Message}", ioEx);
                }
                catch (UnauthorizedAccessException authEx)
                {
                    throw new UnauthorizedAccessException($"[WorldLayer] Access denied for chunk {chunkIndex}: {authEx.Message}", authEx);
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
            }
            else
            {
                if (!_loadingChunks.Contains(chunkIndex))
                {
                    _loadingChunks.Add(chunkIndex);
                    LoadChunkAsync(chunkIndex).Forget();
                }

                return null;
            }
        }

        public void Flush(bool flushToDisk = false)
        {
            foreach (int index in _dirtyChunks)
            {
                if (_loadedChunks.TryGetValue(index, out T[]? chunk))
                {
                    SaveChunkToDisk(index, chunk);
                }
            }

            _dirtyChunks.Clear();
            lock (_ioLock)
            {
                if (_fileStream == null)
                {
                    return;
                }

                if (flushToDisk)
                {
                    _fileStream.Flush(true);
                }
                else
                {
                    _fileStream.Flush();
                }
            }
        }

        public void CompactFile()
        {
            string tempPath = _filePath + ".tmp";
            Flush();

            using (var newLayer = new WorldLayer<T>(tempPath, _widthChunks, _heightChunks, _chunkSize, _maxChunksInMemory))
            {
                for (int i = 0; i < _chunkOffsets.Length; i++)
                {
                    if (_chunkOffsets[i] != -1)
                    {
                        var chunk = LoadChunkFromDisk(i);
                        if (chunk != null && newLayer._fileStream != null)
                        {
                            newLayer._fileStream.Seek(0, SeekOrigin.End);
                            long newOffset = newLayer._fileStream.Position;
                            using var w = new BinaryWriter(newLayer._fileStream, System.Text.Encoding.UTF8, true);
                            newLayer.WriteChunkRLE(w, chunk);
                            newLayer._chunkOffsets[i] = newOffset;
                        }
                    }
                }

                newLayer.SaveOffsetTable();
            }

            _fileStream?.Close();
            File.Replace(tempPath, _filePath, null);
            InitializeFile(); // Re-open
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex)
        {
            if (x < 0 || y < 0 || x >= _widthChunks * _chunkSize || y >= _heightChunks * _chunkSize)
            {
                chunkIndex = -1;
                localIndex = -1;
                return false;
            }

            int cx = x / _chunkSize;
            int cy = y / _chunkSize;
            int lx = x % _chunkSize;
            int ly = y % _chunkSize;

            // Column-major indexing (Original project standard)
            chunkIndex = cy + (cx * _heightChunks);
            localIndex = ly + (lx * _chunkSize);
            return true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "SonarAnalyzer.CSharp",
            "S3877",
            Justification = "Persistent map close failures must propagate instead of becoming silent data loss.")]
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Exception? disposeFailure = null;
            try
            {
                Flush(flushToDisk: true);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ObjectDisposedException)
            {
                disposeFailure = ex;
            }

            lock (_ioLock)
            {
                _disposed = true;
                try
                {
                    _fileStream?.Dispose();
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    disposeFailure ??= ex;
                }
            }

            _loadedChunks.Clear();
            _lruIndexMap.Clear();
            _lruList.Clear();
            _dirtyChunks.Clear();
            _loadingChunks.Clear();

            if (disposeFailure != null)
            {
                throw new IOException(
                    $"[WorldLayer] Failed to persist or close map file '{_filePath}'.",
                    disposeFailure);
            }
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = stream.Read(buffer.Slice(total));
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }

                total += n;
            }
        }

        private static void WriteT(BinaryWriter w, T value)
        {
            Span<T> span = stackalloc T[1];
            span[0] = value;
            w.Write(MemoryMarshal.AsBytes(span));
        }

        private static T ReadT(BinaryReader r)
        {
            int size = Unsafe.SizeOf<T>();
            ReadOnlySpan<byte> bytes = r.ReadBytes(size);
            if (bytes.Length != size)
            {
                throw new EndOfStreamException(
                    $"Expected {size} bytes for a world-layer value, received {bytes.Length}.");
            }

            return MemoryMarshal.Read<T>(bytes);
        }

        private void InitializeFile()
        {
            _fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);

            bool valid = false;
            long offsetTableBytes = (long)_chunkOffsets.Length * sizeof(long);
            if (_fileStream.Length >= HEADER_SIZE)
            {
                try
                {
                    using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                    _fileStream.Seek(0, SeekOrigin.Begin);
                    int w = reader.ReadInt32();
                    int h = reader.ReadInt32();
                    int s = reader.ReadInt32();
                    reader.ReadInt32(); // Reserved

                    if (w == _widthChunks && h == _heightChunks && s == _chunkSize &&
                        _fileStream.Length >= HEADER_SIZE + offsetTableBytes)
                    {
                        var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                        ReadExactly(_fileStream, byteSpan);
                        valid = true;
                    }
                }
                catch (EndOfStreamException)
                {
                    valid = false;
                }
                catch (IOException)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                if (_fileStream.Length > 0)
                {
                    // Fail-fast: a damaged map file must never be silently
                    // recreated as an empty world. Surface the failure instead.
                    _fileStream.Dispose();
                    _fileStream = null;
                    throw new IOException($"Map file '{_filePath}' is corrupt or its header does not match the expected world dimensions. Refusing to recreate it.");
                }

                Array.Fill(_chunkOffsets, -1);
                _fileStream.SetLength(0);
                _fileStream.Seek(0, SeekOrigin.Begin);
                using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
                writer.Write(_widthChunks);
                writer.Write(_heightChunks);
                writer.Write(_chunkSize);
                writer.Write(0);
                var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                _fileStream.Write(byteSpan);
                _fileStream.Flush();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid LoadChunkAsync(int chunkIndex)
        {
            T[]? chunk = null;
            try
            {
                chunk = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(() => LoadChunkFromDisk(chunkIndex));
            }
            catch (IOException ioEx)
            {
                Debug.LogError($"[WorldLayer] Disk I/O error loading chunk {chunkIndex}: {ioEx.Message}");
                _loadingChunks.Remove(chunkIndex);
                throw;
            }
            catch (ObjectDisposedException disposedEx)
            {
                Debug.LogWarning($"[WorldLayer] Stream disposed while loading chunk {chunkIndex}: {disposedEx.Message}");
                _loadingChunks.Remove(chunkIndex);
                throw;
            }
            catch (UnauthorizedAccessException authEx)
            {
                Debug.LogError($"[WorldLayer] Access denied while loading chunk {chunkIndex}: {authEx.Message}");
                _loadingChunks.Remove(chunkIndex);
                throw;
            }
            catch (OutOfMemoryException)
            {
                Debug.LogError($"[WorldLayer] Out of memory while loading chunk {chunkIndex}.");
                _loadingChunks.Remove(chunkIndex);
                throw;
            }

            await Cysharp.Threading.Tasks.UniTask.SwitchToMainThread();

            if (_disposed)
            {
                _loadingChunks.Remove(chunkIndex);
                return;
            }

            // A synchronous request may have filled this slot while the disk
            // read was in flight. Do not overwrite it and, more importantly,
            // do not append a second LRU node for the same chunk.
            if (_loadedChunks.ContainsKey(chunkIndex))
            {
                _loadingChunks.Remove(chunkIndex);
                return;
            }

            _loadingChunks.Remove(chunkIndex);

            // A sparse map is expected while the server is streaming regions.
            // Missing data is not an empty chunk: keep it unloaded so consumers
            // can render the explicit unloaded/black state and retry only after
            // an actual region is received.
            if (chunk == null)
            {
                return;
            }

            AddToCache(chunkIndex, chunk);
            int chunkX = chunkIndex / _heightChunks;
            int chunkY = chunkIndex % _heightChunks;
            ChunkLoaded?.Invoke(
                chunkX * _chunkSize,
                chunkY * _chunkSize,
                _chunkSize,
                _chunkSize);
        }

        private void AddToCache(int chunkIndex, T[] chunk)
        {
            if (_disposed)
            {
                return;
            }

            if (_lruIndexMap.TryGetValue(chunkIndex, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _lruIndexMap.Remove(chunkIndex);
                _loadedChunks.Remove(chunkIndex);
            }

            if (_loadedChunks.Count >= _maxChunksInMemory)
            {
                EvictOldestChunk();
            }

            _loadedChunks[chunkIndex] = chunk;
            var node = _lruList.AddFirst(chunkIndex);
            _lruIndexMap[chunkIndex] = node;
        }

        private void TouchLru(int chunkIndex)
        {
            if (_lruIndexMap.TryGetValue(chunkIndex, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }

        private void EvictOldestChunk()
        {
            if (_lruList.Count == 0 || _lruList.Last == null)
            {
                return;
            }

            int oldestIndex = _lruList.Last.Value;
            if (_dirtyChunks.Contains(oldestIndex))
            {
                SaveChunkToDisk(oldestIndex, _loadedChunks[oldestIndex]);
                _dirtyChunks.Remove(oldestIndex);
            }

            _loadedChunks.Remove(oldestIndex);
            _lruIndexMap.Remove(oldestIndex);
            _lruList.RemoveLast();
        }

        private void MarkDirty(int chunkIndex)
        {
            _dirtyChunks.Add(chunkIndex);
        }

        private T[]? LoadChunkFromDisk(int index)
        {
            if (index < 0 || index >= _chunkOffsets.Length)
            {
                return null;
            }

            lock (_ioLock)
            {
                if (_disposed)
                {
                    return null;
                }

                long offset = _chunkOffsets[index];
                if (offset < 0 || _fileStream == null)
                {
                    return null;
                }

                _fileStream.Seek(offset, SeekOrigin.Begin);
                using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                return ReadChunkRLE(reader);
            }
        }

        private void SaveChunkToDisk(int index, T[] chunk)
        {
            if (_fileStream == null)
            {
                return;
            }

            lock (_ioLock)
            {
                if (_disposed)
                {
                    return;
                }

                _fileStream.Seek(0, SeekOrigin.End);
                long newOffset = _fileStream.Position;

                using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
                WriteChunkRLE(writer, chunk);

                _chunkOffsets[index] = newOffset;

                long tablePos = HEADER_SIZE + (index * sizeof(long));
                _fileStream.Seek(tablePos, SeekOrigin.Begin);
                writer.Write(newOffset);
            }
        }

        private void WriteChunkRLE(BinaryWriter writer, T[] chunk)
        {
            int ptr = 0;
            while (ptr < _chunkArea)
            {
                T current = chunk[ptr];
                ushort count = 1;
                ptr++;
                while (ptr < _chunkArea && count < ushort.MaxValue && chunk[ptr].Equals(current))
                {
                    count++;
                    ptr++;
                }

                writer.Write(count);
                WriteT(writer, current);
            }
        }

        private T[] ReadChunkRLE(BinaryReader reader)
        {
            T[] chunk = new T[_chunkArea];
            int ptr = 0;
            try
            {
                while (ptr < _chunkArea)
                {
                    ushort count = reader.ReadUInt16();
                    T value = ReadT(reader);
                    if (count == 0)
                    {
                        break;
                    }

                    int fill = Math.Min(count, _chunkArea - ptr);
                    chunk.AsSpan(ptr, fill).Fill(value);
                    ptr += fill;
                    if (fill < count)
                    {
                        break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException(
                    $"World layer chunk ended before {_chunkArea} cells were decoded.");
            }

            if (ptr != _chunkArea)
            {
                throw new InvalidDataException(
                    $"World layer chunk contains {ptr} cells; expected {_chunkArea}.");
            }

            return chunk;
        }

        private void SaveOffsetTable()
        {
            if (_fileStream == null)
            {
                return;
            }

            _fileStream.Seek(HEADER_SIZE, SeekOrigin.Begin);
            var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
            _fileStream.Write(byteSpan);
        }
    }
}
