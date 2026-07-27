using System.IO;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class MapStorage : IWorldDataStorage
    {
        private WorldLayer<CellType> _cellLayer;

        public MapStorage()
        {
            Debug.Log($"[MapStorage] Constructor called, instance hash={GetHashCode()}");
        }

        internal void SetAsPending()
        {
            _pendingInstance = this;
        }

        private bool _isInitialized;
        private string _worldCodeName;

        public WorldLayer<CellType> CellLayer => _cellLayer;

        public bool IsReady => _isInitialized && _cellLayer != null;

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
            Debug.Log("[MapStorage] InitWorld START");
            Debug.Log($"[MapStorage] InitWorld — current instance hash={GetHashCode()}, IsDisposed={IsDisposed}, IsInitialized={_isInitialized}, CellLayer={_cellLayer != null}");
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

            var path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";

#if !UNITY_ANDROID || UNITY_EDITOR
            if (!File.Exists(path))
            {
                string sourcePath = $"{Application.streamingAssetsPath}/WorldMaps/{worldCodeName}_cells.mapb";
                if (File.Exists(sourcePath))
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(sourcePath, path, true);
                }
            }
#endif

            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, CHUNK_SIZE);
            _isInitialized = true;
            IsDisposed = false;
            Debug.Log($"[MapStorage] InitWorld END — IsReady={IsReady}, CellLayer hash={_cellLayer.GetHashCode()}");
        }

        public bool IsInitialized() => _isInitialized;

        public string GetWorldCodeName() => _worldCodeName;

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
                _cellLayer[x, y] = type;
                TerrainRenderer.OnCellChanged(x, y);
            }
        }

        public void Dispose()
        {
            Debug.Log("[MapStorage] Dispose called");
            _cellLayer?.Dispose();
            _cellLayer = null;
            _isInitialized = false;
            _worldCodeName = string.Empty;
            IsDisposed = true;
        }
    }
}
