using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using UnityEngine.Rendering;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Renders the world as a background layer using a flat 2D mesh.
    /// Automatically connects to MapStorage and renders behind all other objects.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WorldBackgroundRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Chunk size for mesh generation (should match WorldLayer chunk size)")]
        [SerializeField] private int _chunkSize = 32;
        
        [Tooltip("Render distance in chunks from camera")]
        [SerializeField] private int _renderDistance = 15;
        
        [Tooltip("Cell size in world units")]
        [SerializeField] private float _cellSize = 1.0f;
        
        [Tooltip("Enable debug visualization")]
        [SerializeField] private bool _debugMode = false;

        [Header("Performance")]
        [Tooltip("Enable mesh batching for better performance")]
        [SerializeField] private bool _enableBatching = true;
        
        [Tooltip("Maximum chunks to batch together")]
        [SerializeField] private int _maxBatchSize = 32;

        [Header("Background Settings")]
        [Tooltip("Z position for background rendering (should be behind other objects)")]
        [SerializeField] private float _backgroundZ = -10f;
        
        [Tooltip("Sorting order for background layer")]
        [SerializeField] private int _sortingOrder = -1000;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _backgroundMaterial;
        private bool _materialInitialized = false;
        
        private WorldLayer<CellType> _worldLayer;
        private readonly ConcurrentDictionary<Vector2Int, ChunkMesh> _chunkMeshes = new();
        private readonly HashSet<Vector2Int> _visibleChunks = new();
        
        private Camera _mainCamera;
        private Vector2Int _lastCameraChunk = Vector2Int.zero;
        private bool _isInitialized = false;
        private bool _worldInitialized = false;
        private bool _texturesLoaded = false;
        private bool _atlasTextureApplied = false;
        private float _lastLogTime = 0f;
        private const float _logCooldown = 2.0f; // Log only every 2 seconds
        
        // State management
        private enum InitializationState
        {
            Uninitialized,
            WaitingForWorldInit,
            WaitingForWorldData,
            ReadyForRendering,
            Rendering,
            Failed
        }
        
        private InitializationState _currentState = InitializationState.Uninitialized;
        private float _initializationStartTime = 0f;
        private const float MAX_INITIALIZATION_TIME = 10f; // 10 seconds max initialization time

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            
            if (_meshFilter == null || _meshRenderer == null)
            {
                Debug.LogError("WorldBackgroundRenderer requires MeshFilter and MeshRenderer components");
                enabled = false;
                return;
            }

            _mesh = new Mesh();
            _meshFilter.mesh = _mesh;
            
            _mainCamera = Camera.main;
            
            // Configure as background renderer
            ConfigureBackgroundRendering();
            
            // Subscribe to texture loading events
            WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
            
            // Subscribe to world initialization events
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
                Debug.Log("WorldBackgroundRenderer: Successfully subscribed to MapManager events");
            }
            else
            {
                Debug.LogWarning("WorldBackgroundRenderer: MapManager not found, using fallback initialization");
            }
            
            _isInitialized = true;
            _currentState = InitializationState.WaitingForWorldInit;
            
            // Start aggressive fallback initialization
            StartCoroutine(AggressiveFallbackInitialization());
            
            // Start immediate check for MapStorage availability
            StartCoroutine(ImmediateMapStorageCheck());
            
            Debug.Log("WorldBackgroundRenderer: Initialized, waiting for world data");
        }

        private void ConfigureBackgroundRendering()
        {
            // Set up the renderer for background rendering
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("Universal Render Pipeline/Unlit shader not found. Using default unlit shader.");
                shader = Shader.Find("Unlit/Texture");
            }
            
            _backgroundMaterial = new Material(shader);
            _backgroundMaterial.name = "WorldBackgroundMaterial";
            _backgroundMaterial.renderQueue = 3000; // Ensure it renders after other objects
            
            _meshRenderer.material = _backgroundMaterial;
            _meshRenderer.sortingOrder = _sortingOrder;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            
            // Position the renderer behind everything
            var position = transform.position;
            position.z = _backgroundZ;
            transform.position = position;
            
            _materialInitialized = true;
            Debug.Log("WorldBackgroundRenderer: Material initialized successfully");
        }

        private void Update()
        {
            if (!_isInitialized || _mainCamera == null || !_materialInitialized) return;

            // Track initialization time
            if (_initializationStartTime == 0f)
            {
                _initializationStartTime = Time.time;
            }

            // Check for initialization timeout
            float timeSinceStart = Time.time - _initializationStartTime;
            if (timeSinceStart > MAX_INITIALIZATION_TIME && _currentState != InitializationState.Failed)
            {
                Debug.LogError($"WorldBackgroundRenderer: Initialization timeout after {MAX_INITIALIZATION_TIME}s. Current state: {_currentState}");
                _currentState = InitializationState.Failed;
                
                // Try one final force initialization
                if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
                {
                    Debug.LogWarning("WorldBackgroundRenderer: Attempting final force initialization");
                    _currentState = InitializationState.ReadyForRendering;
                    _worldInitialized = true;
                    InitializeWorldLayer();
                }
            }

            // Initialize world layer if not done yet
            if (!_worldInitialized)
            {
                InitializeWorldLayer();
                
                // Fallback: if we've been waiting too long and have a world layer, force initialization
                if (_worldLayer != null && _currentState == InitializationState.WaitingForWorldData)
                {
                    // Check if we've been waiting for more than 3 seconds (reduced from 5)
                    if (Time.time - _lastLogTime > 3.0f)
                    {
                        Debug.LogWarning("WorldBackgroundRenderer: Forcing initialization due to timeout (3s)");
                        _currentState = InitializationState.ReadyForRendering;
                        _worldInitialized = true;
                    }
                }
            }

            // Only update mesh if we have a world layer and are ready for rendering
            if (_worldLayer != null && _currentState == InitializationState.ReadyForRendering)
            {
                UpdateVisibleChunks();
                UpdateMesh();
            }
            else if (_worldLayer == null && _currentState != InitializationState.Failed)
            {
                // Log only occasionally to prevent spam, but more frequently during active initialization
                if (Time.time - _lastLogTime >= 1.0f) // Reduced from 2.0f to 1.0f
                {
                    Debug.Log($"WorldBackgroundRenderer: Waiting for world layer... State: {_currentState}, Time: {timeSinceStart:F1}s");
                    _lastLogTime = Time.time;
                }
            }
        }

        /// <summary>
        /// Force re-initialization of the world layer (for debugging)
        /// </summary>
        public void ForceReinitialize()
        {
            _worldInitialized = false;
            _worldLayer = null;
            _chunkMeshes.Clear();
            _visibleChunks.Clear();
            _mesh.Clear();
            Debug.Log("WorldBackgroundRenderer: Forced reinitialization");
        }

        /// <summary>
        /// Check if the renderer is properly configured
        /// </summary>
        public bool IsProperlyConfigured()
        {
            return _isInitialized && 
                   _materialInitialized && 
                   _meshFilter != null && 
                   _meshRenderer != null && 
                   _backgroundMaterial != null;
        }

        /// <summary>
        /// Debug method to check initialization status and provide detailed diagnostics
        /// </summary>
        public void DebugInitializationStatus()
        {
            Debug.Log("=== WorldBackgroundRenderer Debug Status ===");
            Debug.Log($"Is Initialized: {_isInitialized}");
            Debug.Log($"Material Initialized: {_materialInitialized}");
            Debug.Log($"World Initialized: {_worldInitialized}");
            Debug.Log($"Current State: {_currentState}");
            Debug.Log($"MapStorage Ready: {MapStorage.Instance?.IsReady ?? false}");
            Debug.Log($"MapStorage World: {MapStorage.Instance?.GetWorldCodeName() ?? "None"}");
            Debug.Log($"MapManager Available: {MapManager.Instance != null}");
            Debug.Log($"Visible Chunks: {_visibleChunks.Count}");
            Debug.Log($"Chunk Meshes: {_chunkMeshes.Count}");
            Debug.Log($"Textures Loaded: {_texturesLoaded}");
            Debug.Log($"Atlas Applied: {_atlasTextureApplied}");
            Debug.Log("==========================================");
        }

        /// <summary>
        /// Manual trigger to force initialization (for debugging)
        /// </summary>
        public void ForceInitialization()
        {
            Debug.Log("WorldBackgroundRenderer: Manual force initialization triggered");
            
            // Reset state
            _worldInitialized = false;
            _worldLayer = null;
            _chunkMeshes.Clear();
            _visibleChunks.Clear();
            _mesh.Clear();
            
            // Try to initialize immediately
            InitializeWorldLayer();
            
            if (_worldLayer != null)
            {
                _currentState = InitializationState.ReadyForRendering;
                _worldInitialized = true;
                Debug.Log("WorldBackgroundRenderer: Force initialization successful");
            }
            else
            {
                Debug.LogWarning("WorldBackgroundRenderer: Force initialization failed - no world data available");
            }
        }

        private void InitializeWorldLayer()
        {
            if (MapStorage.Instance?.cellLayer != null)
            {
                _worldLayer = MapStorage.Instance.cellLayer;
                _worldInitialized = true;
                
                // Update state based on current initialization progress
                if (_currentState == InitializationState.WaitingForWorldInit)
                {
                    _currentState = InitializationState.WaitingForWorldData;
                    Debug.Log($"WorldBackgroundRenderer connected to MapStorage cell layer. State: {_currentState}");
                }
                else if (_currentState == InitializationState.WaitingForWorldData)
                {
                    _currentState = InitializationState.ReadyForRendering;
                    Debug.Log($"WorldBackgroundRenderer: World data ready, transitioning to rendering. State: {_currentState}");
                }
                else if (_currentState == InitializationState.ReadyForRendering)
                {
                    Debug.Log($"WorldBackgroundRenderer: Already ready for rendering. State: {_currentState}");
                }
                
                // Force initial mesh generation now that we have world data
                UpdateVisibleChunks();
            }
            else
            {
                // Only log if enough time has passed to prevent spam
                if (Time.time - _lastLogTime >= _logCooldown)
                {
                    Debug.Log($"WorldBackgroundRenderer: MapStorage not ready, waiting for world initialization... State: {_currentState}");
                    _lastLogTime = Time.time;
                }
            }
        }

        private async void UpdateVisibleChunks()
        {
            if (_worldLayer == null) return;

            var cameraPos = _mainCamera.transform.position;
            var cameraChunkX = Mathf.FloorToInt(cameraPos.x / (_chunkSize * _cellSize));
            var cameraChunkY = Mathf.FloorToInt(cameraPos.y / (_chunkSize * _cellSize));
            var cameraChunk = new Vector2Int(cameraChunkX, cameraChunkY);

            if (cameraChunk == _lastCameraChunk) return;
            _lastCameraChunk = cameraChunk;

            var newVisibleChunks = new HashSet<Vector2Int>();

            // Calculate visible chunk range
            int minX = cameraChunk.x - _renderDistance;
            int maxX = cameraChunk.x + _renderDistance;
            int minY = cameraChunk.y - _renderDistance;
            int maxY = cameraChunk.y + _renderDistance;

            // Generate visible chunks
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var chunkPos = new Vector2Int(x, y);
                    newVisibleChunks.Add(chunkPos);
                }
            }

            // Remove chunks that are no longer visible
            var chunksToRemove = _visibleChunks.Except(newVisibleChunks).ToList();
            foreach (var chunkPos in chunksToRemove)
            {
                _chunkMeshes.TryRemove(chunkPos, out _);
                _visibleChunks.Remove(chunkPos);
            }

            // Add new visible chunks
            var chunksToAdd = newVisibleChunks.Except(_visibleChunks).ToList();
            foreach (var chunkPos in chunksToAdd)
            {
                _visibleChunks.Add(chunkPos);
                await GenerateChunkMeshAsync(chunkPos);
            }
        }

        private async UniTask GenerateChunkMeshAsync(Vector2Int chunkPos)
        {
            if (_chunkMeshes.ContainsKey(chunkPos)) return;

            var chunkMesh = new ChunkMesh(chunkPos, _chunkSize, _cellSize);
            
            // Generate vertices and triangles for the chunk
            await GenerateChunkGeometry(chunkMesh);
            
            // Get texture coordinates for all cells in the chunk
            await GenerateChunkTextures(chunkMesh);

            _chunkMeshes[chunkPos] = chunkMesh;
        }

        private async UniTask GenerateChunkGeometry(ChunkMesh chunkMesh)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            var atlasIndices = new List<int>();

            int vertexIndex = 0;
            int validCells = 0;

            for (int y = 0; y < _chunkSize; y++)
            {
                for (int x = 0; x < _chunkSize; x++)
                {
                    var worldX = chunkMesh.ChunkPosition.x * _chunkSize + x;
                    var worldY = chunkMesh.ChunkPosition.y * _chunkSize + y;

                    try
                    {
                        // Safely get cell type with bounds checking
                        var cellType = MapStorage.Instance.GetCell(worldX, worldY);
                        
                        // Skip unloaded or pregener cells
                        if (cellType == CellType.Unloaded || cellType == CellType.Pregener) continue;

                        // Generate quad vertices
                        var bottomLeft = new Vector3(x * _cellSize, y * _cellSize, 0);
                        var bottomRight = new Vector3((x + 1) * _cellSize, y * _cellSize, 0);
                        var topRight = new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, 0);
                        var topLeft = new Vector3(x * _cellSize, (y + 1) * _cellSize, 0);

                        // Add vertices
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        vertices.Add(topRight);
                        vertices.Add(topLeft);

                        // Add UVs (will be updated later with actual texture coordinates)
                        uvs.Add(Vector2.zero);
                        uvs.Add(Vector2.zero);
                        uvs.Add(Vector2.zero);
                        uvs.Add(Vector2.zero);

                        // Add triangles
                        triangles.Add(vertexIndex);
                        triangles.Add(vertexIndex + 1);
                        triangles.Add(vertexIndex + 2);

                        triangles.Add(vertexIndex);
                        triangles.Add(vertexIndex + 2);
                        triangles.Add(vertexIndex + 3);

                        // Store cell info for texture assignment
                        chunkMesh.Cells.Add(new CellInfo
                        {
                            LocalPosition = new Vector2Int(x, y),
                            WorldPosition = new Vector2Int(worldX, worldY),
                            CellType = cellType,
                            VertexStartIndex = vertexIndex
                        });

                        vertexIndex += 4;
                        validCells++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Error generating geometry for cell at ({worldX}, {worldY}): {ex.Message}");
                    }
                }
            }

            chunkMesh.Vertices = vertices;
            chunkMesh.Triangles = triangles;
            chunkMesh.UVs = uvs;
            chunkMesh.AtlasIndices = atlasIndices;
            
            if (validCells > 0)
            {
                Debug.Log($"Generated chunk mesh for {chunkMesh.ChunkPosition} with {validCells} valid cells");
            }
        }

        private async UniTask GenerateChunkTextures(ChunkMesh chunkMesh)
        {
            if (chunkMesh.Cells.Count == 0) return;

            // Get texture coordinates for all cells in the chunk
            var textureTasks = chunkMesh.Cells.Select(cell => 
                WorldTextureManager.Instance.GetCellTextureCoordinate(cell.CellType, cell.WorldPosition.x, cell.WorldPosition.y)
            ).ToArray();

            var textureCoordinates = await UniTask.WhenAll(textureTasks);

            // Assign texture coordinates and determine atlas indices
            for (int i = 0; i < chunkMesh.Cells.Count; i++)
            {
                var cell = chunkMesh.Cells[i];
                var coord = textureCoordinates[i];

                if (coord == AtlasCoordinate.Empty) continue;

                // Find or create material for this atlas
                int atlasIndex = GetAtlasIndex(coord);
                
                // Update UV coordinates for this cell's quad
                UpdateCellUVs(chunkMesh, cell.VertexStartIndex, coord);

                // Store atlas index for this cell's vertices
                for (int v = 0; v < 4; v++)
                {
                    while (chunkMesh.AtlasIndices.Count <= cell.VertexStartIndex + v)
                    {
                        chunkMesh.AtlasIndices.Add(0);
                    }
                    chunkMesh.AtlasIndices[cell.VertexStartIndex + v] = atlasIndex;
                }
            }
        }

        private int GetAtlasIndex(AtlasCoordinate coord)
        {
            // For now, use the first atlas
            // In the future, this could map to different atlases
            return 0;
        }

        private void UpdateCellUVs(ChunkMesh chunkMesh, int vertexStartIndex, AtlasCoordinate coord)
        {
            if (vertexStartIndex + 3 >= chunkMesh.UVs.Count) return;

            // Update UV coordinates for the quad
            chunkMesh.UVs[vertexStartIndex] = new Vector2(coord.U1, coord.V1);     // Bottom-left
            chunkMesh.UVs[vertexStartIndex + 1] = new Vector2(coord.U2, coord.V1); // Bottom-right
            chunkMesh.UVs[vertexStartIndex + 2] = new Vector2(coord.U2, coord.V2); // Top-right
            chunkMesh.UVs[vertexStartIndex + 3] = new Vector2(coord.U1, coord.V2); // Top-left
        }

        private void UpdateMesh()
        {
            if (_chunkMeshes.Count == 0)
            {
                _mesh.Clear();
                return;
            }

            if (_enableBatching)
            {
                BatchMeshes();
            }
            else
            {
                // Use first chunk as primary mesh for simplicity
                var firstChunk = _chunkMeshes.Values.First();
                ApplyChunkMeshToRenderer(firstChunk);
            }
        }

        private void BatchMeshes()
        {
            var combinedVertices = new List<Vector3>();
            var combinedTriangles = new List<int>();
            var combinedUVs = new List<Vector2>();
            var combinedAtlasIndices = new List<int>();

            int vertexOffset = 0;
            int chunkCount = 0;

            foreach (var chunkMesh in _chunkMeshes.Values)
            {
                if (chunkCount >= _maxBatchSize) break;

                // Offset vertices to world position
                var chunkOffset = new Vector3(
                    chunkMesh.ChunkPosition.x * _chunkSize * _cellSize,
                    chunkMesh.ChunkPosition.y * _chunkSize * _cellSize,
                    0
                );

                // Add vertices with offset
                foreach (var vertex in chunkMesh.Vertices)
                {
                    combinedVertices.Add(vertex + chunkOffset);
                }

                // Add triangles with offset
                foreach (var triangle in chunkMesh.Triangles)
                {
                    combinedTriangles.Add(triangle + vertexOffset);
                }

                // Add UVs
                combinedUVs.AddRange(chunkMesh.UVs);

                // Add atlas indices
                combinedAtlasIndices.AddRange(chunkMesh.AtlasIndices);

                vertexOffset += chunkMesh.Vertices.Count;
                chunkCount++;
            }

            // Apply combined mesh
            _mesh.Clear();
            _mesh.vertices = combinedVertices.ToArray();
            _mesh.triangles = combinedTriangles.ToArray();
            _mesh.uv = combinedUVs.ToArray();
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void ApplyChunkMeshToRenderer(ChunkMesh chunkMesh)
        {
            _mesh.Clear();
            _mesh.vertices = chunkMesh.Vertices.ToArray();
            _mesh.triangles = chunkMesh.Triangles.ToArray();
            _mesh.uv = chunkMesh.UVs.ToArray();
            _mesh.RecalculateNormals();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_debugMode || _worldLayer == null) return;

            Gizmos.color = Color.yellow;
            
            foreach (var chunkPos in _visibleChunks)
            {
                var center = new Vector3(
                    chunkPos.x * _chunkSize * _cellSize + (_chunkSize * _cellSize / 2),
                    chunkPos.y * _chunkSize * _cellSize + (_chunkSize * _cellSize / 2),
                    0
                );
                
                var size = new Vector3(_chunkSize * _cellSize, _chunkSize * _cellSize, 0.1f);
                Gizmos.DrawWireCube(center, size);
            }
        }

        [System.Serializable]
        private class CellInfo
        {
            public Vector2Int LocalPosition;
            public Vector2Int WorldPosition;
            public CellType CellType;
            public int VertexStartIndex;
        }

        [System.Serializable]
        private class ChunkMesh
        {
            public Vector2Int ChunkPosition;
            public List<Vector3> Vertices;
            public List<int> Triangles;
            public List<Vector2> UVs;
            public List<int> AtlasIndices;
            public List<CellInfo> Cells;

            public ChunkMesh(Vector2Int chunkPosition, int chunkSize, float cellSize)
            {
                ChunkPosition = chunkPosition;
                Vertices = new List<Vector3>();
                Triangles = new List<int>();
                UVs = new List<Vector2>();
                AtlasIndices = new List<int>();
                Cells = new List<CellInfo>();
            }
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            // Texture loaded, try to apply atlas texture to material
            _texturesLoaded = true;
            ApplyAtlasTextureToMaterial();
        }

        private async void ApplyAtlasTextureToMaterial()
        {
            if (_atlasTextureApplied || _texturesLoaded == false || _backgroundMaterial == null) return;

            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases != null && atlases.Count > 0)
            {
                var atlas = atlases[0];
                if (atlas != null)
                {
                    var atlasTexture = await atlas.GetAtlasTexture();
                    if (atlasTexture != null)
                    {
                        _backgroundMaterial.mainTexture = atlasTexture;
                        _atlasTextureApplied = true;
                        Debug.Log($"WorldBackgroundRenderer: Atlas texture applied to material. Size: {atlasTexture.width}x{atlasTexture.height}");
                        
                        // Force mesh update to ensure proper rendering
                        UpdateMesh();
                    }
                    else
                    {
                        Debug.LogWarning("WorldBackgroundRenderer: Atlas texture is null");
                    }
                }
                else
                {
                    Debug.LogWarning("WorldBackgroundRenderer: Atlas is null");
                }
            }
            else
            {
                Debug.LogWarning("WorldBackgroundRenderer: No atlases found");
            }
        }

        /// <summary>
        /// Get the number of visible chunks for debugging
        /// </summary>
        public int GetVisibleChunkCount()
        {
            return _visibleChunks.Count;
        }

        /// <summary>
        /// Check if textures have been loaded for debugging
        /// </summary>
        public bool AreTexturesLoaded()
        {
            return _texturesLoaded;
        }

        /// <summary>
        /// Check if atlas texture has been applied for debugging
        /// </summary>
        public bool IsAtlasApplied()
        {
            return _atlasTextureApplied;
        }

        /// <summary>
        /// Get the current renderer state for debugging
        /// </summary>
        public string GetRendererState()
        {
            return _currentState.ToString();
        }

        // Event handlers for world initialization
        private void OnWorldInitialized()
        {
            _currentState = InitializationState.WaitingForWorldData;
            Debug.Log("WorldBackgroundRenderer: World initialized, waiting for world data");
        }

        private void OnWorldDataLoaded()
        {
            _currentState = InitializationState.ReadyForRendering;
            Debug.Log("WorldBackgroundRenderer: World data loaded, ready for rendering");
            
            // Try to initialize world layer immediately
            InitializeWorldLayer();
        }

        private System.Collections.IEnumerator AggressiveFallbackInitialization()
        {
            Debug.Log("WorldBackgroundRenderer: Starting aggressive fallback initialization");
            
            // Wait a frame to ensure other components are initialized
            yield return null;
            
            int attempts = 0;
            const int maxAttempts = 100; // Try for about 10 seconds at 100ms intervals (reduced from 500)
            
            while (attempts < maxAttempts)
            {
                attempts++;
                
                // Check if MapManager is now available
                if (MapManager.Instance != null)
                {
                    Debug.Log("WorldBackgroundRenderer: MapManager found during fallback, subscribing to events");
                    MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
                    MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
                    yield break; // Success, exit coroutine
                }
                
                // Check if MapStorage has data (world was initialized by other means)
                if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
                {
                    Debug.Log("WorldBackgroundRenderer: MapStorage is ready, forcing initialization");
                    _currentState = InitializationState.ReadyForRendering;
                    _worldInitialized = true;
                    InitializeWorldLayer();
                    yield break; // Success, exit coroutine
                }
                
                // Log progress every 5 attempts (0.5 seconds) - more frequent logging
                if (attempts % 5 == 0)
                {
                    Debug.Log($"WorldBackgroundRenderer: Aggressive fallback attempt {attempts}/{maxAttempts} - MapManager: {(MapManager.Instance != null)}, MapStorage: {(MapStorage.Instance?.IsReady ?? false)}");
                }
                
                // Wait before next attempt
                yield return new WaitForSeconds(0.1f);
            }
            
            Debug.LogError("WorldBackgroundRenderer: Aggressive fallback initialization failed after 10 seconds");
            
            // Final attempt: force initialization if MapStorage has any data
            if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
            {
                Debug.LogWarning("WorldBackgroundRenderer: Forcing initialization with available MapStorage data");
                _currentState = InitializationState.ReadyForRendering;
                _worldInitialized = true;
                InitializeWorldLayer();
            }
            else
            {
                Debug.LogError("WorldBackgroundRenderer: Cannot initialize - no MapStorage data available");
                _currentState = InitializationState.Failed;
            }
        }

        private System.Collections.IEnumerator FallbackInitialization()
        {
            Debug.Log("WorldBackgroundRenderer: Starting fallback initialization");
            
            // Wait a frame to ensure other components are initialized
            yield return null;
            
            int attempts = 0;
            const int maxAttempts = 200; // Try for about 20 seconds at 100ms intervals
            
            while (attempts < maxAttempts)
            {
                attempts++;
                
                // Check if MapManager is now available
                if (MapManager.Instance != null)
                {
                    Debug.Log("WorldBackgroundRenderer: MapManager found during fallback, subscribing to events");
                    MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
                    MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
                    yield break; // Success, exit coroutine
                }
                
                // Check if MapStorage has data (world was initialized by other means)
                if (MapStorage.Instance?.cellLayer != null)
                {
                    Debug.Log("WorldBackgroundRenderer: MapStorage has data, forcing initialization");
                    _currentState = InitializationState.ReadyForRendering;
                    _worldInitialized = true;
                    InitializeWorldLayer();
                    yield break; // Success, exit coroutine
                }
                
                // Wait before next attempt
                yield return new WaitForSeconds(0.1f);
            }
            
            Debug.LogError("WorldBackgroundRenderer: Fallback initialization failed after 20 seconds");
            
            // Final attempt: force initialization if MapStorage has any data
            if (MapStorage.Instance?.cellLayer != null)
            {
                Debug.LogWarning("WorldBackgroundRenderer: Forcing initialization with available MapStorage data");
                _currentState = InitializationState.ReadyForRendering;
                _worldInitialized = true;
                InitializeWorldLayer();
            }
        }

        /// <summary>
        /// Immediate check for MapStorage availability - runs every frame until MapStorage is ready
        /// </summary>
        private System.Collections.IEnumerator ImmediateMapStorageCheck()
        {
            Debug.Log("WorldBackgroundRenderer: Starting immediate MapStorage check");
            
            int frameCount = 0;
            const int maxFrames = 300; // Check for about 5 seconds at 60 FPS
            
            while (frameCount < maxFrames)
            {
                frameCount++;
                
                // Check if MapStorage is ready
                if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
                {
                    Debug.Log($"WorldBackgroundRenderer: MapStorage became ready after {frameCount} frames ({frameCount/60.0f:F1}s)");
                    
                    // If we're still waiting for world init, transition to data waiting
                    if (_currentState == InitializationState.WaitingForWorldInit)
                    {
                        _currentState = InitializationState.WaitingForWorldData;
                    }
                    
                    // Initialize immediately
                    InitializeWorldLayer();
                    
                    yield break; // Success, exit coroutine
                }
                
                // Check if MapManager became available
                if (MapManager.Instance != null)
                {
                    Debug.Log($"WorldBackgroundRenderer: MapManager became available after {frameCount} frames ({frameCount/60.0f:F1}s)");
                    yield break; // Let the event handlers take over
                }
                
                // Yield every few frames to avoid blocking
                if (frameCount % 10 == 0)
                {
                    yield return null;
                }
            }
            
            Debug.Log("WorldBackgroundRenderer: Immediate MapStorage check completed without success");
        }

        private void OnDestroy()
        {
            // Unsubscribe from events to prevent memory leaks
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized -= OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;
            }
        }
    }
}
