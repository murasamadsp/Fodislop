#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player.Logic;
using Fodinae.World.Lighting;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.World.Terrain
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour, ICachedCellDataProvider
    {
        private const int TerrainRegionAnchorCells = 8;
        private const int DimensionAllocationQuantum = 32;
        private const int MaximumTerrainDimension = 384;
        private const float DimensionGrowDelay = 0.4f;

        public static TerrainRenderer? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Instance = null;
        }

        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = GameConstants.World.CellSize;
        [SerializeField]
        private Shader? _terrainShader;
        [SerializeField]
        private Color _shimmerHighlightColor = Color.white;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = -1000;
        [SerializeField]
        private int _viewportPadding = 2;

        private MeshFilter? _meshFilter;
        private MeshRenderer? _meshRenderer;

        [Inject]
        private IWorldDataStorage? _storage;

        [Inject]
        private MapManager? _mapManager;

        [Inject]
        private ITextureService? _textureService;

        private Mesh? _mesh;
        private Camera? _mainCamera;

        private TerrainCellCache _cellCache = new();
        private TerrainPrecalculator _precalc = new();
        private TerrainMeshBuilder _meshBuilder = new();
        private BackgroundFloodFill _backgroundFloodFill = new();

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();
        private readonly RenderTargetIdentifier[] _lightingFieldTargets = new RenderTargetIdentifier[2];
        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private int _lastRequestedWidth;
        private int _lastRequestedHeight;
        private float _lastViewportSizeChangeTime;
        private bool _isInitialized = false;
        private bool _subscribedEvents = false;
        private bool _needsRefresh = false;
        private bool _textureRefreshPending;
        private float _nextTextureRefreshTime;
        private bool _useColorLod = false;
        private int _lastAtlasCount = -1;
        private bool _lightingBindingValidated;
        private WorldLayer<CellType>? _subscribedCellLayer;

        private static readonly VertexAttributeDescriptor[] VertexLayout = new VertexAttributeDescriptor[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4),
        };
        private const MeshUpdateFlags UPLOAD_FLAGS =
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
        private static readonly ProfilerMarker CacheMarker = new("Fodinae.Terrain.Cache");
        private static readonly ProfilerMarker PrecalculateMarker = new("Fodinae.Terrain.Precalculate");
        private static readonly ProfilerMarker FloodFillMarker = new("Fodinae.Terrain.BackgroundFloodFill");
        private static readonly ProfilerMarker MeshBuildMarker = new("Fodinae.Terrain.MeshBuild");
        private static readonly ProfilerMarker MeshUploadMarker = new("Fodinae.Terrain.MeshUpload");
        private static readonly ProfilerMarker TerrainLateUpdateMarker =
            new("Fodinae.Terrain.LateUpdate.CPU");
        private ulong _lightingGeometryRevision = 1;

        public CachedCellInfo GetCell(int x, int y)
        {
            var c = _cellCache.GetCellData(x, y);
            return new CachedCellInfo { Type = c.Type, Properties = c.Properties };
        }

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public bool IsReadyForGameplay =>
            _isInitialized &&
            _mesh != null &&
            _mesh.vertexCount > 0 &&
            _materials.Length > 0;

        public static void OnCellChanged(int x, int y)
        {
            Instance?.HandleCellChanged(x, y);
        }

        public static void OnRegionChanged(
            int startX,
            int startY,
            int width,
            int height)
        {
            Instance?.HandleRegionChanged(startX, startY, width, height);
        }

        private void HandleCellChanged(int serverX, int serverY)
        {
            HandleRegionChanged(serverX, serverY, 1, 1);
        }

        private void HandleRegionChanged(
            int serverX,
            int serverY,
            int width,
            int height)
        {
            if (_mapManager == null || _lastGridPos.x == int.MinValue)
            {
                _needsRefresh = true;
                return;
            }

            int lastServerY = serverY + Mathf.Max(0, height - 1);
            int firstUnityY = Mathf.FloorToInt(
                CoordinateUtils.ServerToUnityY(serverY, _mapManager.WorldHeight));
            int lastUnityY = Mathf.FloorToInt(
                CoordinateUtils.ServerToUnityY(lastServerY, _mapManager.WorldHeight));
            int minimumUnityY = Mathf.Min(firstUnityY, lastUnityY);
            int maximumUnityY = Mathf.Max(firstUnityY, lastUnityY);
            TerrariaLightingEngine.Instance?.InvalidateRegion(
                serverX,
                minimumUnityY,
                width,
                maximumUnityY - minimumUnityY + 1);
            bool affectsCachedTerrain =
                serverX + width - 1 >= _lastGridPos.x - 1 &&
                serverX <= _lastGridPos.x + _meshWidth &&
                maximumUnityY >= _lastGridPos.y - 1 &&
                minimumUnityY <= _lastGridPos.y + _meshHeight;
            if (affectsCachedTerrain)
            {
                _lightingGeometryRevision++;
                _needsRefresh = true;
            }
        }

        protected void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyTerrainObject(gameObject);
                return;
            }

            Instance = this;
            InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = Camera.main;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
                if (_meshFilter != null)
                {
                    _meshFilter.mesh = _mesh;
                }
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = true;
                _meshRenderer.sortingLayerName = _sortingLayerName;
                _meshRenderer.sortingOrder = _sortingOrder;
            }
        }

        protected void Start()
        {
            if (_mainCamera != null)
            {
                return;
            }

            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            }
        }

        public void EnsureSubscriptions()
        {
            SubscribeToCellLayer();
            if (_subscribedEvents)
            {
                return;
            }

            if (_textureService is WorldTextureManager wtm)
            {
                wtm.OnTextureLoaded += OnTextureLoaded;
            }

            if (_mapManager != null)
            {
                _mapManager.OnWorldDataLoaded += OnWorldDataLoaded;
            }

            _subscribedEvents = true;
        }

        protected void OnDestroy()
        {
            if (_textureService is WorldTextureManager wtm)
            {
                wtm.OnTextureLoaded -= OnTextureLoaded;
            }

            if (_mapManager != null)
            {
                _mapManager.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnCellLayerChunkLoaded;
                _subscribedCellLayer = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            if (_mesh != null)
            {
                DestroyTerrainObject(_mesh);
                _mesh = null;
            }

            CleanupMaterials();
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain);
                if (_terrainShader == null || !_terrainShader.isSupported)
                {
                    throw new InvalidOperationException(
                        $"Required terrain shader '{ProjectRuntimeContracts.ShaderNames.Terrain}' " +
                        "is missing or unsupported. World lighting cannot run without it.");
                }
            }
        }

        private int _diagLogged;

        private void LogDiag(int bit, string message)
        {
            if ((_diagLogged & bit) != 0)
            {
                return;
            }

            _diagLogged |= bit;
            Debug.Log(message);
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            if ((_diagLogged & (1 << 9)) == 0)
            {
                LogDiag(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");
            }

            if (filename.StartsWith("Cells/", StringComparison.OrdinalIgnoreCase))
            {
                InitializeShader();
                _cellCache.ClearCaches();

                // Cell textures arrive independently. Rebuilding the complete
                // viewport mesh for every arrival caused hundreds of native
                // mesh uploads while the world asset set streamed in.
                _textureRefreshPending = true;
            }
        }

        private void OnWorldDataLoaded()
        {
            SubscribeToCellLayer();
            _needsRefresh = true;
            _lightingGeometryRevision++;
            TerrariaLightingEngine.Instance?.InvalidateStaticCache();
        }

        private void SubscribeToCellLayer()
        {
            WorldLayer<CellType>? cellLayer = _storage?.CellLayer;
            if (ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                return;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnCellLayerChunkLoaded;
            }

            _subscribedCellLayer = cellLayer;
            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded += OnCellLayerChunkLoaded;
            }
        }

        private void OnCellLayerChunkLoaded(int serverX, int serverY, int width, int height)
        {
            HandleRegionChanged(serverX, serverY, width, height);
        }

        protected void LateUpdate()
        {
            using var terrainLateUpdateMarker = TerrainLateUpdateMarker.Auto();
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                if ((_diagLogged & (1 << 0)) == 0)
                {
                    LogDiag(1 << 0, $"[TerrainDiag] gate BLOCKED: mapManager={(_mapManager == null ? "NULL" : "ok")}, storage={(_storage == null ? "NULL" : (_storage.IsReady ? "ready" : "NOT_READY"))}");
                }

                return;
            }

            if (PlayerMovementController.LocalPlayer is not { HasServerPosition: true })
            {
                return;
            }

            if ((_diagLogged & (1 << 1)) == 0)
            {
                LogDiag(1 << 1, "[TerrainDiag] gate passed: storage ready");
            }

            if (_meshFilter != null && _meshFilter.sharedMesh != _mesh)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            if (_mainCamera == null)
            {
                LogDiag(1 << 2, "[TerrainDiag] camera NULL");
                return;
            }

            if (_textureRefreshPending && Time.unscaledTime >= _nextTextureRefreshTime)
            {
                _textureRefreshPending = false;
                _nextTextureRefreshTime = Time.unscaledTime + 1f;
                _needsRefresh = true;
            }

            if ((_diagLogged & (1 << 3)) == 0)
            {
                LogDiag(1 << 3, $"[TerrainDiag] camera ok: {_mainCamera.name} at {_mainCamera.transform.position}");
            }

            TerrariaLightingEngine lightingEngine = TerrariaLightingEngine.Instance ??
                throw new InvalidOperationException(
                    "TerrariaLightingEngine was not initialized by GameLifetimeScope.");
            int effectiveViewportPadding = _viewportPadding;
            int requiredLightingPadding = lightingEngine.RequiredTerrainPadding +
                TerrainRegionAnchorCells + lightingEngine.StableRegionPaddingCells;
            effectiveViewportPadding = Mathf.Max(
                effectiveViewportPadding,
                requiredLightingPadding);

            int requestedWidth = Mathf.Clamp(
                Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) +
                    (effectiveViewportPadding * 2),
                2,
                MaximumTerrainDimension);
            int requestedHeight = Mathf.Clamp(
                Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) +
                    (effectiveViewportPadding * 2),
                2,
                MaximumTerrainDimension);
            if (requestedWidth != _lastRequestedWidth || requestedHeight != _lastRequestedHeight)
            {
                _lastRequestedWidth = requestedWidth;
                _lastRequestedHeight = requestedHeight;
                _lastViewportSizeChangeTime = Time.unscaledTime;
            }

            bool viewportSizeSettled =
                !Application.isPlaying ||
                Time.unscaledTime - _lastViewportSizeChangeTime >= DimensionGrowDelay;
            int targetWidth = SelectCachedDimension(
                requestedWidth,
                _meshWidth,
                _isInitialized,
                viewportSizeSettled);
            int targetHeight = SelectCachedDimension(
                requestedHeight,
                _meshHeight,
                _isInitialized,
                viewportSizeSettled);

            bool dimensionsChanged = targetWidth != _meshWidth || targetHeight != _meshHeight;
            if (dimensionsChanged || !_isInitialized)
            {
                _meshWidth = targetWidth;
                _meshHeight = targetHeight;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
                _cellCache.EnsureCapacity(_meshWidth, _meshHeight);
                _precalc.EnsureCapacity(_meshWidth, _meshHeight);
                _meshBuilder.EnsureCapacity(_meshWidth, _meshHeight, _cellSize);
                _backgroundFloodFill.Allocate(_meshWidth, _meshHeight);

                _needsRefresh = true;
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int desiredGridPos = new Vector2Int(
                Mathf.FloorToInt(camPos.x / _cellSize) - (_meshWidth / 2),
                Mathf.FloorToInt(camPos.y / _cellSize) - (_meshHeight / 2));
            int regionAnchor = Mathf.Clamp(
                TerrainRegionAnchorCells,
                1,
                Mathf.Max(1, effectiveViewportPadding));

            // Keep the current terrain region while the camera remains inside
            // it. The old implementation snapped the desired origin every
            // eight cells, which rebuilt the whole mesh/cache/SDF on a fixed
            // cadence even though the existing mesh still covered the camera.
            // Recenter only when the actual visible viewport reaches an edge.
            int viewportWidth = Mathf.Max(2, requestedWidth - (effectiveViewportPadding * 2));
            int viewportHeight = Mathf.Max(2, requestedHeight - (effectiveViewportPadding * 2));
            int viewportMinX = Mathf.FloorToInt(camPos.x / _cellSize) - (viewportWidth / 2);
            int viewportMinY = Mathf.FloorToInt(camPos.y / _cellSize) - (viewportHeight / 2);
            bool regionOutsideViewport =
                _lastGridPos.x == int.MinValue ||
                viewportMinX < _lastGridPos.x ||
                viewportMinY < _lastGridPos.y ||
                viewportMinX + viewportWidth > _lastGridPos.x + _meshWidth ||
                viewportMinY + viewportHeight > _lastGridPos.y + _meshHeight;

            Vector2Int currentGridPos = regionOutsideViewport || dimensionsChanged
                ? new Vector2Int(
                    SnapRegionCoordinate(desiredGridPos.x, regionAnchor),
                    SnapRegionCoordinate(desiredGridPos.y, regionAnchor))
                : _lastGridPos;

            bool terrainWasRebuilt = currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged;
            if (terrainWasRebuilt)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                if (!_needsRefresh)
                {
                    // The lighting material field is rasterized from this
                    // mesh. A streamed chunk can change occupancy at the
                    // cache edge without changing the camera lighting
                    // region, so every successful mesh rebuild must publish a
                    // new geometry revision for normal/AO caches as well.
                    _lightingGeometryRevision++;
                }

                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
            }

            // Lighting follows the actual camera viewport, not the cached terrain origin.
            int lightingPadding = requiredLightingPadding;
            int lightingMinX = viewportMinX - lightingPadding;
            int lightingMinY = viewportMinY - lightingPadding;
            int lightingWidth = viewportWidth + (lightingPadding * 2);
            int lightingHeight = viewportHeight + (lightingPadding * 2);
            lightingEngine.UpdateLighting(
                lightingMinX,
                lightingMinY,
                lightingWidth,
                lightingHeight,
                _storage,
                _mapManager);
            ValidateLightingBinding();
        }

        private void ValidateLightingBinding()
        {
            if (_lightingBindingValidated || _materials.Length == 0)
            {
                return;
            }

            for (int materialIndex = 0; materialIndex < _materials.Length; materialIndex++)
            {
                Material material = _materials[materialIndex];
                if (material.FindPass("Universal2D") < 0 ||
                    material.FindPass("LightingMaterialField") < 0)
                {
                    throw new InvalidOperationException(
                        $"Terrain material '{material.name}' is missing world-lighting passes.");
                }
            }

            Texture globalTexture = Shader.GetGlobalTexture("_WorldLightTexture");
            Vector4 globalRect = Shader.GetGlobalVector("_WorldLightRect");
            if (globalTexture == null || globalRect.z <= 0f || globalRect.w <= 0f)
            {
                throw new InvalidOperationException(
                    "Radiance Cascades completed without publishing a valid world light texture and rect.");
            }

            _lightingBindingValidated = true;
            Debug.Log(
                $"[TerrainLighting] Bound {globalTexture.name} " +
                $"({globalTexture.width}x{globalTexture.height}) to {_materials.Length} terrain material(s).");
        }

        private static int SnapRegionCoordinate(int coordinate, int anchor)
        {
            return Mathf.FloorToInt(coordinate / (float)anchor) * anchor;
        }

        private static int SelectCachedDimension(
            int requested,
            int current,
            bool initialized,
            bool viewportSizeSettled)
        {
            int allocationSteps = Mathf.CeilToInt(requested / (float)DimensionAllocationQuantum);
            int allocated = Mathf.Min(
                MaximumTerrainDimension,
                allocationSteps * DimensionAllocationQuantum);
            if (!initialized)
            {
                return allocated;
            }

            // Zoom changes continuously. Growing the mesh immediately at each
            // 32-cell boundary turns one zoom gesture into several full CPU
            // rebuilds. Keep the previous mesh while the viewport is moving
            // and allocate once after the zoom settles.
            if (requested > current)
            {
                return viewportSizeSettled ? allocated : current;
            }

            return viewportSizeSettled && current - requested >= DimensionAllocationQuantum
                ? allocated
                : current;
        }

        public void RenderLightingMaterialFields(
            CommandBuffer commandBuffer,
            RenderTexture materialField,
            RenderTexture emissionField,
            Vector4 worldRect)
        {
            if (_mesh == null || _materials.Length == 0 ||
                !materialField.IsCreated() || !emissionField.IsCreated())
            {
                throw new InvalidOperationException(
                    "Terrain material fields cannot be rendered before the terrain mesh and targets are ready.");
            }

            _lightingFieldTargets[0] = new RenderTargetIdentifier(materialField);
            _lightingFieldTargets[1] = new RenderTargetIdentifier(emissionField);
            commandBuffer.SetRenderTarget(
                _lightingFieldTargets,
                new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);

            Matrix4x4 projection = Matrix4x4.Ortho(
                worldRect.x,
                worldRect.x + worldRect.z,
                worldRect.y,
                worldRect.y + worldRect.w,
                -100f,
                100f);
            commandBuffer.SetViewProjectionMatrices(
                Matrix4x4.identity,
                GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));

            int subMeshCount = Mathf.Min(_mesh.subMeshCount, _materials.Length);
            int materialFieldPass = _materials[0].FindPass("LightingMaterialField");
            if (materialFieldPass < 0)
            {
                throw new InvalidOperationException(
                    $"Terrain material '{_materials[0].name}' is missing the LightingMaterialField pass.");
            }

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material material = _materials[subMeshIndex];
                commandBuffer.DrawMesh(
                    _mesh,
                    transform.localToWorldMatrix,
                    material,
                    subMeshIndex,
                    materialFieldPass);
            }

            commandBuffer.GenerateMips(materialField);
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            if ((_diagLogged & (1 << 4)) == 0)
            {
                LogDiag(1 << 4, $"[TerrainDiag] UpdateVertexAttributes min=({minX},{minY}) size={_meshWidth}x{_meshHeight}");
            }

            var wtm = _textureService as WorldTextureManager;
            if (wtm == null || _mapManager == null || _storage == null)
            {
                if ((_diagLogged & (1 << 5)) == 0)
                {
                    LogDiag(1 << 5, $"[TerrainDiag] BAIL: wtm={(wtm == null ? "NULL" : "ok")} mapManager={(_mapManager == null ? "NULL" : "ok")}");
                }

                return;
            }

            var atlases = wtm.GetAllAtlases();
            if (atlases == null || atlases.Count == 0)
            {
                LogDiag(1 << 6, "[TerrainDiag] BAIL: atlases empty");
                return;
            }

            if ((_diagLogged & (1 << 7)) == 0)
            {
                LogDiag(1 << 7, $"[TerrainDiag] atlases: {atlases.Count}");
            }

            bool materialsChanged = false;
            if (atlases.Count != _lastAtlasCount)
            {
                _lastAtlasCount = atlases.Count;
                _lightingBindingValidated = false;
                _cellCache.ClearCaches();
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                int estimatedPerAtlas = (_meshWidth * _meshHeight * 2 * 6 / atlases.Count) + 16;
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new List<int>(estimatedPerAtlas);
                    _materials[i] = new Material(_terrainShader);
                    if (_materials[i].FindPass("Universal2D") < 0 ||
                        _materials[i].FindPass("LightingMaterialField") < 0)
                    {
                        throw new InvalidOperationException(
                            $"Terrain material '{_materials[i].name}' is missing required " +
                            "world-lighting properties or passes.");
                    }
                }

                materialsChanged = true;
            }
            else
            {
                int estimatedPerAtlas =
                    (_meshWidth * _meshHeight * 2 * 6 / _subMeshIndices.Length) + 16;
                foreach (var list in _subMeshIndices)
                {
                    list.Clear();
                    if (list.Capacity < estimatedPerAtlas)
                    {
                        list.Capacity = estimatedPerAtlas;
                    }
                }
            }

            wtm.FlushDirtyAtlases();

            try
            {
                int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
                int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
                bool canScrollCache =
                    !_needsRefresh &&
                    _cellCache.CacheMinX != int.MinValue &&
                    Mathf.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
                    Mathf.Abs(cacheDeltaY) < _cellCache.CacheHeight;
                using (CacheMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _cellCache.ScrollAndFill(cacheDeltaX, cacheDeltaY, _storage, _mapManager, wtm, atlases);
                    }
                    else
                    {
                        _cellCache.PopulateFull(minX, minY, _storage, _mapManager, wtm, atlases);
                    }
                }

                using (PrecalculateMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _precalc.PrecalculateIncremental(_cellCache, _meshWidth, _meshHeight, cacheDeltaX, cacheDeltaY);
                    }
                    else
                    {
                        _precalc.PrecalculateFull(_cellCache, _meshWidth, _meshHeight);
                    }
                }

                using (FloodFillMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _backgroundFloodFill.ComputeIncremental(cacheDeltaX, cacheDeltaY, this);
                    }
                    else
                    {
                        _backgroundFloodFill.ComputeFull(this);
                    }
                }

                using (MeshBuildMarker.Auto())
                {
                    _meshBuilder.BuildFull(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _subMeshIndices, _useColorLod, _mapManager, wtm);
                }

                if (_mesh != null)
                {
                    using (MeshUploadMarker.Auto())
                    {
                        _mesh.Clear();
                        _mesh.subMeshCount = atlases.Count;
                        _mesh.SetVertexBufferParams(_meshBuilder.VertexBuffer.Length, VertexLayout);
                        _mesh.SetVertexBufferData(
                            _meshBuilder.VertexBuffer,
                            0,
                            0,
                            _meshBuilder.VertexBuffer.Length,
                            0,
                            UPLOAD_FLAGS);

                        // The terrain is a regular viewport-sized grid. Scanning
                        // every vertex after each rebuild is wasted CPU work and
                        // becomes noticeable with the bounded terrain cache. Keep a
                        // conservative local-space bound that also contains the
                        // relief offsets and the two terrain layers.
                        _mesh.bounds = new Bounds(
                            new Vector3(_meshWidth * _cellSize * 0.5f, _meshHeight * _cellSize * 0.5f, 0f),
                            new Vector3(
                                (_meshWidth * _cellSize) + (_cellSize * 2f),
                                (_meshHeight * _cellSize) + (_cellSize * 2f),
                                2f));
                        if ((_diagLogged & (1 << 8)) == 0)
                        {
                            LogDiag(
                                1 << 8,
                                "[TerrainDiag] BuildFull: grid=(" +
                                $"{_lastGridPos.x},{_lastGridPos.y}) " +
                                $"world={_mapManager.WorldWidth}x{_mapManager.WorldHeight} " +
                                $"verts={_meshBuilder.VertexBuffer.Length} meshVerts={_mesh.vertexCount} " +
                                $"bounds={_mesh.bounds} transform={transform.position}");
                        }

                        for (int i = 0; i < atlases.Count; i++)
                        {
                            var atlasTex = atlases[i].Texture;
                            if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                            {
                                _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                                _materials[i].SetTexture("_BaseMap", atlasTex);
                            }

                            if (_materials[i].GetTexture("_FlowMap") != wtm.FlowMapTexture)
                            {
                                _materials[i].SetTexture("_FlowMap", wtm.FlowMapTexture);
                            }

                            _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                        }
                    }
                }

                _needsRefresh = false;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[TerrainRenderer] Build failed: grid=({minX},{minY}) " +
                    $"size={_meshWidth}x{_meshHeight}, world=" +
                    $"{_mapManager?.WorldWidth ?? 0}x{_mapManager?.WorldHeight ?? 0}, " +
                    $"atlases={(_textureService as WorldTextureManager)?.GetAllAtlases().Count ?? 0}, " +
                    $"storageReady={_storage?.IsReady ?? false}.");
                Debug.LogException(ex);
                _needsRefresh = true;
            }

            bool needReassignMaterials = materialsChanged;
            if (!needReassignMaterials && _meshRenderer != null)
            {
                var sharedMats = _meshRenderer.sharedMaterials;
                if (sharedMats == null || sharedMats.Length != _materials.Length)
                {
                    needReassignMaterials = true;
                }
                else
                {
                    for (int i = 0; i < _materials.Length; i++)
                    {
                        if (sharedMats[i] != _materials[i])
                        {
                            needReassignMaterials = true;
                            break;
                        }
                    }
                }
            }

            if (needReassignMaterials && _meshRenderer != null)
            {
                _meshRenderer.sharedMaterials = _materials;
            }
        }

        private void CleanupMaterials()
        {
            if (_materials != null)
            {
                foreach (var mat in _materials)
                {
                    DestroyTerrainObject(mat);
                }
            }
        }

        private static void DestroyTerrainObject(UnityEngine.Object? obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj, allowDestroyingAssets: true);
            }
        }
    }
}
