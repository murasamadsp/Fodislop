using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class MapStorage
    {
        private static MapStorage _instance;
        public static MapStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MapStorage();
                }
                return _instance;
            }
        }

        public WorldLayer<CellType> cellLayer;
        private bool _isInitialized = false;
        private string _worldCodeName;

        public bool IsReady => _isInitialized && cellLayer != null;

        public void InitWorld(string worldCodeName, int width, int height)
        {
            if (_isInitialized)
            {
                Debug.LogWarning($"MapStorage already initialized for world: {_worldCodeName}");
                return;
            }

            // Validate input parameters
            if (string.IsNullOrEmpty(worldCodeName))
            {
                Debug.LogError("MapStorage.InitWorld: World code name cannot be null or empty");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"MapStorage.InitWorld: Invalid world dimensions: {width}x{height}. Dimensions must be positive.");
                return;
            }

            _worldCodeName = worldCodeName;
            var path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
            Debug.Log($"Initializing cell storage at: {path} for world '{worldCodeName}' with dimensions {width}x{height}");
            
            try
            {
                // Use configurable chunk size (default 32) - should match WorldLayer default
                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;
                
                Debug.Log($"Calculating chunks: width={width}, height={height}, chunkSize={chunkSize}");
                Debug.Log($"Resulting chunks: {widthChunks}x{heightChunks} = {widthChunks * heightChunks} total chunks");
                
                // Validate chunk calculations
                if (widthChunks <= 0 || heightChunks <= 0)
                {
                    Debug.LogError($"MapStorage.InitWorld: Invalid chunk calculation. widthChunks={widthChunks}, heightChunks={heightChunks}");
                    return;
                }

                // Check for potential memory issues with very large maps
                long totalChunks = (long)widthChunks * heightChunks;
                if (totalChunks > 1000000) // 1M chunks limit
                {
                    Debug.LogWarning($"MapStorage.InitWorld: Very large map detected ({totalChunks} chunks). This may cause performance issues.");
                }

                cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;
                
                Debug.Log($"MapStorage initialized successfully for world '{worldCodeName}' with dimensions {width}x{height} ({widthChunks}x{heightChunks} chunks)");
                Debug.Log($"WorldLayer created with path: {path}, chunkSize: {chunkSize}");
            }
            catch (System.IO.IOException ioEx)
            {
                Debug.LogError($"MapStorage.InitWorld: File I/O error while creating WorldLayer for '{worldCodeName}': {ioEx.Message}");
                Debug.LogError($"File path: {path}");
                _isInitialized = false;
                cellLayer = null;
            }
            catch (System.ArgumentException argEx)
            {
                Debug.LogError($"MapStorage.InitWorld: Invalid arguments for WorldLayer creation: {argEx.Message}");
                Debug.LogError($"World: {worldCodeName}, Width: {width}, Height: {height}");
                _isInitialized = false;
                cellLayer = null;
            }
            catch (System.OutOfMemoryException memEx)
            {
                Debug.LogError($"MapStorage.InitWorld: Out of memory while creating WorldLayer for '{worldCodeName}': {memEx.Message}");
                Debug.LogError($"Requested memory for {width}x{height} world may be too large");
                _isInitialized = false;
                cellLayer = null;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"MapStorage.InitWorld: Unexpected error while initializing world '{worldCodeName}': {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
                _isInitialized = false;
                cellLayer = null;
            }
        }

        /// <summary>
        /// Safely get cell type from the world layer with proper null checks
        /// </summary>
        /// <param name="x">World X coordinate</param>
        /// <param name="y">World Y coordinate</param>
        /// <returns>CellType or CellType.Unloaded if not ready</returns>
        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || cellLayer == null)
            {
                return CellType.Unloaded;
            }

            try
            {
                return cellLayer[x, y];
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error accessing cell at ({x}, {y}): {ex.Message}");
                return CellType.Unloaded;
            }
        }

        /// <summary>
        /// Set cell type in the world layer with proper null checks
        /// </summary>
        /// <param name="x">World X coordinate</param>
        /// <param name="y">World Y coordinate</param>
        /// <param name="cellType">Cell type to set</param>
        public void SetCell(int x, int y, CellType cellType)
        {
            if (!_isInitialized || cellLayer == null)
            {
                Debug.LogWarning($"Cannot set cell at ({x}, {y}) - MapStorage not initialized");
                return;
            }

            try
            {
                cellLayer[x, y] = cellType;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error setting cell at ({x}, {y}) to {cellType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get world information safely
        /// </summary>
        public string GetWorldCodeName() => _worldCodeName;
        public bool IsInitialized() => _isInitialized;

        private MapStorage() 
        {
            // Initialize in uninitialized state
            _isInitialized = false;
            _worldCodeName = string.Empty;
        }

        public void Dispose()
        {
            try
            {
                cellLayer?.Dispose();
                cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                Debug.Log("MapStorage disposed successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error disposing MapStorage: {ex.Message}");
            }
        }
    }
}
