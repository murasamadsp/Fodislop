using MinesServer.Data;
using System.IO;
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
        public bool _isInitialized = false;
        public string _worldCodeName;

        public bool IsReady => _isInitialized && cellLayer != null;

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Debug.Log($"[MapStorage] InitWorld called: world='{worldCodeName}', dimensions={width}x{height}");
            
            // CRITICAL: Reset state before initialization attempt
            Dispose(); // Ensure clean state
            
            // Validate input parameters
            if (string.IsNullOrEmpty(worldCodeName))
            {
                Debug.LogError("[MapStorage] World code name cannot be null or empty");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[MapStorage] Invalid world dimensions: {width}x{height}. Dimensions must be positive.");
                return;
            }

            _worldCodeName = worldCodeName;
            var path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
            Debug.Log($"[MapStorage] Initializing cell storage at: {path} for world '{worldCodeName}' with dimensions {width}x{height}");
            
            try
            {
                // Use configurable chunk size (default 32) - should match WorldLayer default
                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;
                
                Debug.Log($"Calculating chunks: width={width}, height={height}, chunkSize={chunkSize}");
                Debug.Log($"Resulting chunks: {widthChunks}x{heightChunks} = {widthChunks * heightChunks} total chunks");
                
                // Validate chunk calculations - CRITICAL FIX
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

                // Additional validation for WorldLayer constructor parameters
                if (widthChunks > 100000 || heightChunks > 100000)
                {
                    Debug.LogError($"MapStorage.InitWorld: World dimensions too large for WorldLayer. Max supported: 100000x100000 chunks");
                    return;
                }

                if (chunkSize <= 0 || chunkSize > 1024)
                {
                    Debug.LogError($"MapStorage.InitWorld: Invalid chunk size: {chunkSize}. Must be between 1 and 1024.");
                    return;
                }

                // Check if directory exists and is writable
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        Debug.Log($"Created directory: {directory}");
                    }
                    catch (System.Exception dirEx)
                    {
                        Debug.LogError($"MapStorage.InitWorld: Cannot create directory '{directory}': {dirEx.Message}");
                        Debug.LogError($"This is CRITICAL - terrain rendering will fail without proper directory access");
                        
                        // Try fallback directory
                        string fallbackDir = Path.Combine(Application.persistentDataPath, "fallback");
                        try
                        {
                            Directory.CreateDirectory(fallbackDir);
                            path = Path.Combine(fallbackDir, $"{worldCodeName}_cells.mapb");
                            Debug.LogWarning($"MapStorage.InitWorld: Using fallback directory: {path}");
                        }
                        catch
                        {
                            Debug.LogError("MapStorage.InitWorld: Fallback directory creation also failed");
                            _isInitialized = false;
                            return;
                        }
                    }
                }

                // Check if file already exists and handle appropriately
                if (File.Exists(path))
                {
                    Debug.Log($"MapStorage.InitWorld: Existing world file found at {path}, will be used");
                }

                // CRITICAL: Enhanced WorldLayer creation with comprehensive error handling
                try
                {
                    Debug.Log($"[MapStorage] Creating WorldLayer with parameters: path={path}, widthChunks={widthChunks}, heightChunks={heightChunks}, chunkSize={chunkSize}");
                    
                    // CRITICAL FIX: Ensure proper parameter order and validation
                    // WorldLayer constructor expects: path, widthChunks, heightChunks, chunkSize
                    cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                    
                    // CRITICAL: Verify the WorldLayer was created successfully
                    if (cellLayer == null)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer creation failed - returned null");
                        Debug.LogError($"[MapStorage] This is a fundamental failure - terrain rendering cannot work");
                        _isInitialized = false;
                        return;
                    }
                    
                    Debug.Log($"[MapStorage] WorldLayer created successfully");
                    Debug.Log($"[MapStorage] WorldLayer verification: WidthChunks={cellLayer.WidthChunks}, HeightChunks={cellLayer.HeightChunks}, ChunkSize={cellLayer.ChunkSize}");
                    
                    // Additional verification - CRITICAL CHECK
                    if (cellLayer.WidthChunks != widthChunks || cellLayer.HeightChunks != heightChunks)
                    {
                        Debug.LogWarning($"[MapStorage] WorldLayer dimensions don't match expected: expected {widthChunks}x{heightChunks}, got {cellLayer.WidthChunks}x{cellLayer.HeightChunks}");
                    }
                    
                    // CRITICAL: Verify cellLayer is not null and has valid data
                    if (cellLayer == null)
                    {
                        Debug.LogError("[MapStorage] CRITICAL: cellLayer is null after WorldLayer creation");
                        Debug.LogError("[MapStorage] This will cause terrain rendering to fail completely");
                        _isInitialized = false;
                        return;
                    }
                    
                    // Test basic cell access to verify WorldLayer is functional
                    try
                    {
                        var testCell = cellLayer[0, 0];
                        Debug.Log($"[MapStorage] WorldLayer basic cell access test passed: {testCell}");
                    }
                    catch (System.Exception cellTestEx)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer cell access failed: {cellTestEx.Message}");
                        Debug.LogError("[MapStorage] WorldLayer appears to be created but not functional");
                        _isInitialized = false;
                        cellLayer?.Dispose();
                        cellLayer = null;
                        return;
                    }
                    
                }
                catch (System.Exception worldLayerEx)
                {
                    Debug.LogError($"[MapStorage] CRITICAL: WorldLayer constructor failed for '{worldCodeName}': {worldLayerEx.Message}");
                    Debug.LogError($"[MapStorage] WorldLayer constructor parameters: path={path}, widthChunks={widthChunks}, heightChunks={heightChunks}, chunkSize={chunkSize}");
                    Debug.LogError($"[MapStorage] WorldLayer exception type: {worldLayerEx.GetType().Name}");
                    
                    // Provide specific guidance based on exception type
                    if (worldLayerEx is System.IO.IOException)
                    {
                        Debug.LogError("[MapStorage] This is likely a file I/O issue. Check disk space and file permissions.");
                        Debug.LogError("[MapStorage] CRITICAL: Without proper file access, terrain rendering will fail completely");
                        
                        // Try creating a test world in memory as fallback
                        TryCreateFallbackWorld(worldCodeName, width, height);
                    }
                    else if (worldLayerEx is System.ArgumentException)
                    {
                        Debug.LogError("[MapStorage] This is likely an invalid parameter issue. Check world dimensions and chunk size.");
                        Debug.LogError("[MapStorage] CRITICAL: Invalid parameters prevent WorldLayer creation, stopping terrain rendering");
                        
                        // Try with different chunk size
                        TryCreateWithDifferentChunkSize(worldCodeName, width, height);
                    }
                    else if (worldLayerEx is System.OutOfMemoryException)
                    {
                        Debug.LogError("[MapStorage] This is a memory issue. The world may be too large for available memory.");
                        Debug.LogError("[MapStorage] CRITICAL: Insufficient memory prevents terrain rendering");
                        
                        // Try creating a smaller test world
                        TryCreateSmallerTestWorld(worldCodeName);
                    }
                    else
                    {
                        Debug.LogError($"[MapStorage] Unexpected WorldLayer creation error: {worldLayerEx.GetType().Name}");
                        Debug.LogError("[MapStorage] CRITICAL: Unknown error prevents terrain rendering");
                        
                        // Try emergency fallback
                        TryEmergencyFallback(worldCodeName, width, height);
                    }
                    
                    _isInitialized = false;
                    cellLayer?.Dispose();
                    cellLayer = null;
                    return;
                }
                
                _isInitialized = true;
                
                Debug.Log($"[MapStorage] SUCCESS: MapStorage initialized successfully for world '{worldCodeName}' with dimensions {width}x{height} ({widthChunks}x{heightChunks} chunks)");
                Debug.Log($"[MapStorage] WorldLayer created with path: {path}, chunkSize: {chunkSize}");
                
                // Final verification that everything is ready - CRITICAL CHECK
                if (IsReady)
                {
                    Debug.Log($"[MapStorage] VERIFICATION: MapStorage is fully ready for terrain rendering");
                    Debug.Log($"[MapStorage] Ready state: IsReady={IsReady}, IsInitialized={IsInitialized()}, cellLayer={(cellLayer != null ? "not null" : "NULL")}");
                    
                    // CRITICAL: Log detailed cellLayer information for debugging
                    if (cellLayer != null)
                    {
                        Debug.Log($"[MapStorage] cellLayer details: WidthChunks={cellLayer.WidthChunks}, HeightChunks={cellLayer.HeightChunks}, ChunkSize={cellLayer.ChunkSize}");
                    }
                }
                else
                {
                    Debug.LogError($"[MapStorage] CRITICAL: MapStorage initialization completed but not ready for terrain rendering");
                    Debug.LogError($"[MapStorage] This indicates a fundamental problem - terrain rendering will fail");
                    Debug.LogError($"[MapStorage] Ready state: IsReady={IsReady}, IsInitialized={IsInitialized()}, cellLayer={(cellLayer != null ? "not null" : "NULL")}");
                    
                    // CRITICAL: If we got here, something is wrong with our initialization
                    if (cellLayer == null)
                    {
                        Debug.LogError("[MapStorage] CRITICAL: cellLayer is NULL - WorldLayer creation failed");
                    }
                    else
                    {
                        Debug.LogError("[MapStorage] CRITICAL: cellLayer exists but IsReady is false");
                        Debug.LogError($"[MapStorage] cellLayer state: WidthChunks={cellLayer.WidthChunks}, HeightChunks={cellLayer.HeightChunks}");
                    }
                }
            }
            catch (System.IO.IOException ioEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: File I/O error while creating WorldLayer for '{worldCodeName}': {ioEx.Message}");
                Debug.LogError($"[MapStorage] File path: {path}");
                Debug.LogError($"[MapStorage] IOException details: {ioEx.GetType().Name} - {ioEx.Message}");
                Debug.LogError("[MapStorage] CRITICAL: File I/O errors prevent terrain rendering");
                
                // Try fallback mechanisms
                TryCreateFallbackWorld(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.ArgumentException argEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Invalid arguments for WorldLayer creation: {argEx.Message}");
                Debug.LogError($"[MapStorage] World: {worldCodeName}, Width: {width}, Height: {height}, WidthChunks: {(width + 32 - 1) / 32}, HeightChunks: {(height + 32 - 1) / 32}");
                Debug.LogError("[MapStorage] CRITICAL: Invalid arguments prevent terrain rendering");

                // Try with different parameters
                TryCreateWithDifferentChunkSize(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.OutOfMemoryException memEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Out of memory while creating WorldLayer for '{worldCodeName}': {memEx.Message}");
                Debug.LogError($"[MapStorage] Requested memory for {width}x{height} world may be too large");
                Debug.LogError($"[MapStorage] Available memory: {System.GC.GetTotalMemory(false)} bytes");
                Debug.LogError("[MapStorage] CRITICAL: Memory issues prevent terrain rendering");

                // Try creating a smaller world
                TryCreateSmallerTestWorld(worldCodeName);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.UnauthorizedAccessException authEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Access denied while creating WorldLayer for '{worldCodeName}': {authEx.Message}");
                Debug.LogError($"[MapStorage] Check file permissions for path: {path}");
                Debug.LogError("[MapStorage] CRITICAL: Permission issues prevent terrain rendering");

                // Try different location
                TryCreateFallbackWorld(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.Security.SecurityException secEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Security exception while creating WorldLayer for '{worldCodeName}': {secEx.Message}");
                Debug.LogError("[MapStorage] Check application permissions for file access");
                Debug.LogError("[MapStorage] CRITICAL: Security restrictions prevent terrain rendering");

                // Try emergency fallback
                TryEmergencyFallback(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Unexpected error while initializing world '{worldCodeName}': {ex.Message}");
                Debug.LogError($"[MapStorage] Exception type: {ex.GetType().Name}");
                Debug.LogError($"[MapStorage] Stack trace: {ex.StackTrace}");

                // Try to provide more context about the failure
                if (ex.InnerException != null)
                {
                    Debug.LogError($"[MapStorage] Inner exception: {ex.InnerException.Message}");
                    Debug.LogError($"[MapStorage] Inner exception type: {ex.InnerException.GetType().Name}");
                }

                Debug.LogError("[MapStorage] CRITICAL: Unknown error prevents terrain rendering");

                // Try emergency fallback
                TryEmergencyFallback(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
        }

        /// <summary>
        /// Try creating a fallback world when primary initialization fails
        /// </summary>
        private void TryCreateFallbackWorld(string worldCodeName, int width, int height)
        {
            Debug.LogWarning($"[MapStorage] Attempting fallback world creation for '{worldCodeName}'");
            
            try
            {
                // Try creating in a different location
                string fallbackPath = Path.Combine(Application.temporaryCachePath, $"{worldCodeName}_fallback_cells.mapb");
                Debug.LogWarning($"[MapStorage] Using fallback path: {fallbackPath}");
                
                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;
                
                cellLayer = new WorldLayer<CellType>(fallbackPath, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;
                
                Debug.Log($"[MapStorage] Fallback world created successfully at {fallbackPath}");
            }
            catch (System.Exception fallbackEx)
            {
                Debug.LogError($"[MapStorage] Fallback world creation failed: {fallbackEx.Message}");
            }
        }

        /// <summary>
        /// Try creating world with different chunk size when primary fails
        /// </summary>
        private void TryCreateWithDifferentChunkSize(string worldCodeName, int width, int height)
        {
            Debug.LogWarning($"[MapStorage] Attempting world creation with different chunk size for '{worldCodeName}'");
            
            int[] chunkSizes = { 16, 64, 128 };
            
            foreach (int chunkSize in chunkSizes)
            {
                try
                {
                    string path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
                    int widthChunks = (width + chunkSize - 1) / chunkSize;
                    int heightChunks = (height + chunkSize - 1) / chunkSize;
                    
                    cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                    _isInitialized = true;
                    
                    Debug.Log($"[MapStorage] World created successfully with chunk size {chunkSize}");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MapStorage] Chunk size {chunkSize} failed: {ex.Message}");
                }
            }
            
            Debug.LogError("[MapStorage] All chunk size attempts failed");
        }

        /// <summary>
        /// Try creating a smaller test world when memory issues occur
        /// </summary>
        private void TryCreateSmallerTestWorld(string worldCodeName)
        {
            Debug.LogWarning($"[MapStorage] Creating smaller test world for '{worldCodeName}' due to memory issues");
            
            try
            {
                string path = $"{Application.persistentDataPath}/{worldCodeName}_test_cells.mapb";
                const int testWidth = 64;
                const int testHeight = 64;
                const int chunkSize = 32;
                
                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;
                
                cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;
                
                Debug.Log($"[MapStorage] Smaller test world created: {testWidth}x{testHeight}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Smaller test world creation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Emergency fallback that creates a minimal in-memory world
        /// </summary>
        private void TryEmergencyFallback(string worldCodeName, int width, int height)
        {
            Debug.LogError($"[MapStorage] Attempting emergency fallback for '{worldCodeName}'");
            
            try
            {
                // Create a minimal test world that should always work
                string path = $"{Application.persistentDataPath}/emergency_test_cells.mapb";
                const int testWidth = 32;
                const int testHeight = 32;
                const int chunkSize = 32;
                
                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;
                
                cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;
                _worldCodeName = "emergency_test";
                
                Debug.Log($"[MapStorage] Emergency fallback world created: {testWidth}x{testHeight}");
                Debug.Log("[MapStorage] This should allow terrain rendering to proceed");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Emergency fallback failed: {ex.Message}");
                Debug.LogError("[MapStorage] Terrain rendering system is completely broken");
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
