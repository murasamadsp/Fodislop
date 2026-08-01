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
        private string? _worldCodeName;

        public WorldLayer<CellType>? CellLayer => _cellLayer;

        public string MapFilePath => _mapFilePath ?? string.Empty;

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

#if UNITY_EDITOR
        public void EnsureEditorInitialized()
        {
            if (_isInitialized || Application.isPlaying)
            {
                return;
            }

            InitWorld("EditorPreview", 128, 128);
        }
#endif

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Dispose();

            if (string.IsNullOrEmpty(worldCodeName))
            {
                Debug.LogError("[MapStorage] World code name cannot be null or empty");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[MapStorage] Invalid world dimensions: {width}x{height}");
                return;
            }

            _worldCodeName = worldCodeName;
            const int CHUNK_SIZE = 32;
            int widthChunks = (width + CHUNK_SIZE - 1) / CHUNK_SIZE;
            int heightChunks = (height + CHUNK_SIZE - 1) / CHUNK_SIZE;

            if (widthChunks <= 0 || heightChunks <= 0)
            {
                Debug.LogError($"[MapStorage] Invalid chunk calculation: {widthChunks}x{heightChunks}");
                return;
            }

            string path = Path.Combine(Application.persistentDataPath, worldCodeName + MapExtension);
            _mapFilePath = path;

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, CHUNK_SIZE);
            _isInitialized = true;
            IsDisposed = false;
            Revision++;
        }

        public bool IsInitialized() => _isInitialized;

        public string GetWorldCodeName() => _worldCodeName ?? string.Empty;

        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                return CellType.Unloaded;
            }

            return _cellLayer.GetCell(x, y, touchLru: true);
        }

        public void SetCell(int x, int y, CellType type)
        {
            if (_isInitialized && _cellLayer != null)
            {
                if (_cellLayer.GetCell(x, y, touchLru: false) == type)
                {
                    return;
                }

                _cellLayer[x, y] = type;
                Revision++;
                TerrainRenderer.OnCellChanged(x, y);
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
            catch (IOException ioEx)
            {
                Debug.LogError($"[MapStorage] Map flush failed; dirty chunks were retained: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException authEx)
            {
                Debug.LogError($"[MapStorage] Map flush access denied; dirty chunks were retained: {authEx.Message}");
            }
            catch (ObjectDisposedException disposedEx)
            {
                Debug.LogError($"[MapStorage] Map stream was disposed before flush: {disposedEx.Message}");
            }
        }

        public void Dispose()
        {
            _cellLayer?.Dispose();
            _cellLayer = null;
            _isInitialized = false;
            _worldCodeName = string.Empty;
            _mapFilePath = null;
            IsDisposed = true;
            Revision++;
        }
    }
}
