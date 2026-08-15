#nullable enable

using System;
using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    public class MapStorage : IWorldDataStorage
    {
        private WorldLayer<CellType>? _cellLayer;
        private string? _mapFilePath;

        private const string MapExtension = ".map";
        private const string BackupMapSuffix = ".backup.map";

        public MapStorage()
        {
        }

        internal void SetAsPending()
        {
        }

        private bool _isInitialized;
        private string _worldCodeName = string.Empty;
        private int _worldWidth;
        private int _worldHeight;

        public WorldLayer<CellType>? CellLayer => _cellLayer;

        public string MapFilePath => _mapFilePath ?? throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

        public string BackupMapFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_worldCodeName))
                {
                    return string.Empty;
                }

                return Path.Combine(Application.persistentDataPath, _worldCodeName + BackupMapSuffix);
            }
        }

        public bool IsReady => _isInitialized && _cellLayer != null;
        public bool HasDirtyChunks => _cellLayer?.HasDirtyChunks == true;

        public long Revision { get; private set; }

        public bool IsDisposed { get; private set; }

        public void EnsureEditorInitialized()
        {
#if UNITY_EDITOR
            if (_isInitialized || Application.isPlaying)
            {
                return;
            }

            InitWorld("EditorPreview", 128, 128);
#else
            throw new InvalidOperationException(
                "[MapStorage] EnsureEditorInitialized is available only in the Unity Editor.");
#endif
        }

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Dispose();

            if (string.IsNullOrEmpty(worldCodeName))
            {
                throw new ArgumentException("[MapStorage] World code name cannot be null or empty", nameof(worldCodeName));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException($"[MapStorage] Invalid world dimensions: {width}x{height}");
            }

            _worldCodeName = worldCodeName;
            _worldWidth = width;
            _worldHeight = height;
            const int CHUNK_SIZE = 32;
            int widthChunks = (width + CHUNK_SIZE - 1) / CHUNK_SIZE;
            int heightChunks = (height + CHUNK_SIZE - 1) / CHUNK_SIZE;

            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new ArgumentOutOfRangeException($"[MapStorage] Invalid chunk calculation: {widthChunks}x{heightChunks}");
            }

            string path = Path.Combine(Application.persistentDataPath, worldCodeName + MapExtension);
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, CHUNK_SIZE);
                _mapFilePath = path;
                _isInitialized = true;
                IsDisposed = false;
                Revision++;
            }
            catch (IOException ioEx)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw new IOException($"[MapStorage] Could not open map file '{path}': {ioEx.Message}", ioEx);
            }
            catch (UnauthorizedAccessException authEx)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw new UnauthorizedAccessException($"[MapStorage] Access denied for map file '{path}': {authEx.Message}", authEx);
            }
            catch (OutOfMemoryException)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw;
            }
        }

        public bool IsInitialized() => _isInitialized;

        public string GetWorldCodeName() => _worldCodeName;

        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException("[MapStorage] GetCell called before world initialization");
            }

            return _cellLayer.GetCell(x, y, touchLru: true);
        }

        public void SetCell(int x, int y, CellType type)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException(
                    $"[MapStorage] SetCell called before world initialization: ({x},{y}).");
            }

            if (_cellLayer.GetCellSync(x, y, touchLru: true) == type)
            {
                return;
            }

            _cellLayer[x, y] = type;
            Revision++;
            TerrainRenderer.OnCellChanged(x, y);
        }

        public void SetRegion(
            int startX,
            int startY,
            int width,
            int height,
            CellType[] cells)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException(
                    $"[MapStorage] SetRegion called before world initialization: " +
                    $"({startX},{startY}) {width}x{height}.");
            }

            long expectedCellCount = (long)width * height;
            if (width <= 0 || height <= 0 || cells.Length < expectedCellCount)
            {
                throw new ArgumentException(
                    $"[MapStorage] Invalid region ({startX},{startY}) {width}x{height}: " +
                    $"payload has {cells.Length} cells, expected at least {expectedCellCount}.",
                    nameof(cells));
            }

            if (startX < 0 || startY < 0 || startX >= _worldWidth || startY >= _worldHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startX),
                    "[MapStorage] Region " +
                    $"({startX},{startY}) {width}x{height} " +
                    $"is outside world bounds {_worldWidth}x{_worldHeight}.");
            }

            int appliedWidth = Math.Min(width, _worldWidth - startX);
            int appliedHeight = Math.Min(height, _worldHeight - startY);
            if (appliedWidth != width || appliedHeight != height)
            {
                Debug.LogWarning(
                    $"[MapStorage] Clipping padded edge region ({startX},{startY}) " +
                    $"{width}x{height} to {appliedWidth}x{appliedHeight} " +
                    $"for world {_worldWidth}x{_worldHeight}.");
            }

            bool changed = false;
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    CellType type = cells[index++];
                    if (x >= appliedWidth || y >= appliedHeight)
                    {
                        continue;
                    }

                    int worldX = startX + x;
                    int worldY = startY + y;
                    if (_cellLayer.GetCellSync(worldX, worldY, touchLru: true) == type)
                    {
                        continue;
                    }

                    _cellLayer[worldX, worldY] = type;
                    changed = true;
                }
            }

            if (changed)
            {
                Revision++;
                TerrainRenderer.OnRegionChanged(startX, startY, width, height);
            }
        }

        /// <summary>
        /// Persists all dirty map chunks immediately.
        /// The layer normally flushes on chunk eviction and dispose, but the
        /// application can be paused or terminated while dirty chunks are
        /// still resident in the RAM cache.
        /// </summary>
        public void Flush()
        {
            if (_cellLayer == null || !_isInitialized || IsDisposed)
            {
                return;
            }

            try
            {
                _cellLayer.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ObjectDisposedException)
            {
                throw new IOException(
                    $"[MapStorage] Failed to persist map '{MapFilePath}'. " +
                    "The world cannot continue with unsaved chunks.",
                    ex);
            }
        }

        public void Dispose()
        {
            Exception? disposeFailure = null;
            try
            {
                _cellLayer?.Dispose();
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ObjectDisposedException)
            {
                disposeFailure = ex;
            }
            finally
            {
                _cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                _worldWidth = 0;
                _worldHeight = 0;
                _mapFilePath = null;
                IsDisposed = true;
                Revision++;
            }

            if (disposeFailure != null)
            {
                throw new IOException(
                    "[MapStorage] Failed to close the persistent world map after flushing.",
                    disposeFailure);
            }
        }
    }
}
