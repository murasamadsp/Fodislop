#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World.Lighting;
using Fodinae.World.Lighting.Quality;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.World.Terrain
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour, ICachedCellDataProvider
    {
        private const int TerrainRegionAnchorCells = 8;
        private const int DimensionAllocationQuantum = 32;
        private const int MaximumTerrainDimension = 384;
        private const float DimensionGrowDelay = 0.4f;

        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = ProjectRuntimeContracts.World.CellSize;
        [SerializeField]
        private Shader? _terrainShader;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = -1000;
        [SerializeField]
        private int _viewportPadding = 2;

        private MeshFilter? _meshFilter;
        private MeshRenderer? _meshRenderer;

        [Inject]
        private IWorldDataStorage _storage = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private ITextureService _textureService = null!;

        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

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
        private bool _needsRefresh = false;
        private readonly HashSet<CellType> _pendingTextureCellTypes = [];

        // Pending patches, kept as separate rectangles rather than one union.
        //
        // They used to be a single min/max rectangle. That is correct for a
        // player mining one cell, and badly wrong for streamed chunks: they
        // arrive scattered across the viewport, each one small, and unioning
        // two of them at opposite corners produces a rectangle covering the
        // whole screen. The size check below then measured that union, decided
        // the patch was not worth it, and handed the frame to the full
        // rebuild - which is how the debug overlay came to read 9 rebuilds and
        // 9 full repopulations. Every single one. The check was doing what it
        // says; the number it was given did not describe the actual work.
        private readonly DirtyRectSet _dirtyRects = new();
        private bool _useColorLod = false;
        private int _lastAtlasCount = -1;
        private bool _lightingBindingValidated;
        private bool _fatalBuildError;
        private WorldLayer<CellType>? _subscribedCellLayer;
        private WorldTextureManager? _subscribedTextureManager;
        private MapManager? _subscribedMapManager;
        private IWorldDataStorage? _subscribedStorage;

        private static readonly VertexAttributeDescriptor[] VertexLayout =
        [
            new(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color,     VertexAttributeFormat.UNorm8,  4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2), // quad UV          16 → 4 bytes
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4), // atlasRect        16 → 8 bytes
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4), // tileSizeVec      16 → 8 bytes
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4), // worldPos: stays float32 (coords > 2048)
            new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float16, 4), // animData         16 → 8 bytes
            new(VertexAttribute.TexCoord5, VertexAttributeFormat.Float16, 4), // anchorData       16 → 8 bytes
            new(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4), // glowVec: stays float32 (packed RGB > 65504)
        ];
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

        public static bool BypassCpuMeshRebuild { get; set; }
        public static bool BypassTerrainDraw { get; set; }

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public bool IsReadyForGameplay =>
            _isInitialized &&
            _mesh != null &&
            _mesh.vertexCount > 0 &&
            _materials.Length > 0;

        public void ApplyClientConfig()
        {
            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires an initialized ClientConfig.");

            bool enableDistortion = config.EnableTerrainDistortion;
            if (_precalc.EnableDistortion != enableDistortion)
            {
                _precalc.EnableDistortion = enableDistortion;
                _needsRefresh = true;
            }

            if (_materials.Length == 0)
            {
                // The config is the source of truth. Atlas materials are created
                // asynchronously when the first server textures arrive and read
                // this config during creation; an early UI change must not make
                // the pause menu fail just because that pipeline is not ready.
                return;
            }

            foreach (Material material in _materials)
            {
                material.SetVector("_FlowScale", config.TerrainFlowScale);
                material.SetFloat("_ShimmerSpeedScale", config.TerrainShimmerSpeedScale);
                material.SetFloat("_PulseSpeedScale", config.TerrainPulseSpeedScale);
                material.SetColor("_ShimmerColor", config.TerrainShimmerColor);
                material.SetColor("_DebugColor", config.TerrainDebugColor);
                material.SetFloat("_DebugMode", config.TerrainDebugMode ? 1f : 0f);
            }
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
            bool affectsCachedTerrain =
                serverX + width - 1 >= _lastGridPos.x - 1 &&
                serverX <= _lastGridPos.x + _meshWidth &&
                maximumUnityY >= _lastGridPos.y - 1 &&
                minimumUnityY <= _lastGridPos.y + _meshHeight;
            if (!affectsCachedTerrain)
            {
                return;
            }

            _lightingEngine?.InvalidateRegion(
                serverX,
                minimumUnityY,
                width,
                maximumUnityY - minimumUnityY + 1);

            // Every region change - a mined cell or a streamed 32x32 chunk -
            // goes into the same dirty rectangle, and LateUpdate patches it
            // through the incremental path.
            //
            // Size used to decide this instead. Anything over
            // BulkRegionCellThreshold set _needsRefresh, and _needsRefresh is
            // what disables the cache scroll, so one streamed chunk repopulated
            // the entire viewport: cache, precalculation, background flood fill
            // and mesh build, four Parallel.For passes over the whole grid,
            // plus a vertex upload of all of it. The comment that used to sit
            // here recorded the measurement - 37 full repopulations out of 42
            // rebuilds while walking, "essentially the entire main thread".
            //
            // Coalescing those bursts made them periodic rather than rare: a
            // walking player streams chunks continuously, so the debounce below
            // kept hitting its own 0.25s ceiling and fired a full parallel
            // repopulate four times a second, indefinitely. Coalescing was
            // treating the symptom. The cost was never the burst, it was that a
            // change of any size took the full path at all - and the rect-based
            // path this now takes already exists, already unions rectangles,
            // and already serves player edits every frame.
            //
            // The rect is clipped to the cached region on the way in rather
            // than at use: a streamed chunk routinely straddles the cache edge,
            // and UpdateDirtyCells turns these bounds into array offsets
            // without clamping them itself. DirtyRectSet.Add does the clipping
            // in long arithmetic, so a chunk rectangle with absurd extents is
            // rejected rather than wrapping into a valid-looking one.
            _dirtyRects.Add(
                new RectInt(
                    serverX,
                    minimumUnityY,
                    width,
                    maximumUnityY + 1 - minimumUnityY),
                new RectInt(_lastGridPos.x, _lastGridPos.y, _meshWidth, _meshHeight));
        }

        protected void Awake()
        {
            InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = _gameplayCamera?.Camera;

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
            _mainCamera = _gameplayCamera?.Camera;
        }

        public void InitializeEditorPreview(IWorldDataStorage storage, MapManager mapManager, ITextureService textureService)
        {
            _storage = storage;
            _mapManager = mapManager;
            _textureService = textureService;
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mainCamera == null)
            {
                _mainCamera = _gameplayCamera?.Camera;
            }

            InitializeShader();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
            }

            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = true;
                _meshRenderer.sortingLayerName = _sortingLayerName;
                _meshRenderer.sortingOrder = _sortingOrder;
            }

            EnsureSubscriptions();
            _needsRefresh = true;
        }

        public void EnsureSubscriptions()
        {
            SubscribeToCellLayer();
            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged -= HandleCellChanged;
                _subscribedStorage.RegionChanged -= HandleRegionChanged;
            }

            _subscribedStorage = _storage;
            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged += HandleCellChanged;
                _subscribedStorage.RegionChanged += HandleRegionChanged;
            }

            if (_subscribedTextureManager != null)
            {
                _subscribedTextureManager.OnTextureLoaded -= OnTextureLoaded;
            }

            _subscribedTextureManager = _textureService as WorldTextureManager;
            if (_subscribedTextureManager != null)
            {
                _subscribedTextureManager.OnTextureLoaded += OnTextureLoaded;
            }

            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            _subscribedMapManager = _mapManager;
            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded += OnWorldDataLoaded;
            }
        }

        protected void OnDestroy()
        {
            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged -= HandleCellChanged;
                _subscribedStorage.RegionChanged -= HandleRegionChanged;
                _subscribedStorage = null;
            }

            if (_subscribedTextureManager != null)
            {
                _subscribedTextureManager.OnTextureLoaded -= OnTextureLoaded;
                _subscribedTextureManager = null;
            }

            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded -= OnWorldDataLoaded;
                _subscribedMapManager = null;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnCellLayerChunkLoaded;
                _subscribedCellLayer = null;
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
        private bool _invalidGridPositionLogged;

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
                int extensionIndex = filename.LastIndexOf('.');
                ReadOnlySpan<char> id = filename.AsSpan(
                    "Cells/".Length,
                    (extensionIndex >= 0 ? extensionIndex : filename.Length) - "Cells/".Length);
                if (int.TryParse(id, out int cellTypeId) &&
                    (uint)cellTypeId <= ushort.MaxValue)
                {
                    _pendingTextureCellTypes.Add((CellType)cellTypeId);
                }
            }
        }

        private void OnWorldDataLoaded()
        {
            SubscribeToCellLayer();
            _needsRefresh = true;
            _lightingGeometryRevision++;
            _lightingEngine?.InvalidateStaticCache();
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
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = TerrainLateUpdateMarker.Auto();
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                return;
            }

            if (_localPlayer is not { Current: { HasServerPosition: true } })
            {
                return;
            }

            if ((_diagLogged & (1 << 1)) == 0)
            {
                LogDiag(1 << 1, "[TerrainDiag] gate passed: storage ready");
            }

            EnsureMeshAndFilter();

            if (!TryResolveCamera())
            {
                return;
            }

            LightingEngine? lightingEngine = ResolveLightingEngine();
            if (lightingEngine == null)
            {
                return;
            }

            UpdateViewportDimensions(
                lightingEngine,
                out int requestedWidth,
                out int requestedHeight,
                out int effectiveViewportPadding,
                out bool dimensionsChanged);

            ResolveGridPosition(
                requestedWidth,
                requestedHeight,
                effectiveViewportPadding,
                dimensionsChanged,
                out Vector2Int currentGridPos,
                out int viewportMinX,
                out int viewportMinY,
                out int viewportWidth,
                out int viewportHeight);

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw;
            }

            if (_pendingTextureCellTypes.Count > 0 && !BypassCpuMeshRebuild)
            {
                UpdateTextureCells(currentGridPos.x, currentGridPos.y);
                _pendingTextureCellTypes.Clear();
            }

            CoalesceOversizedDirtyRects();

            RebuildOrPatchTerrain(currentGridPos, dimensionsChanged);

            PublishLightingUpdate(lightingEngine, viewportMinX, viewportMinY, viewportWidth, viewportHeight);
        }

        private void EnsureMeshAndFilter()
        {
            InitializeShader();
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
                if (_meshFilter != null)
                {
                    _meshFilter.sharedMesh = _mesh;
                }
            }
            else if (_meshFilter != null && _meshFilter.sharedMesh != _mesh)
            {
                _meshFilter.sharedMesh = _mesh;
            }
        }

        private bool TryResolveCamera()
        {
            Camera? resolvedCam = _gameplayCamera?.Camera;
            if (resolvedCam != null)
            {
                _mainCamera = resolvedCam;
            }

            if (_mainCamera == null)
            {
                LogDiag(1 << 2, "[TerrainDiag] camera NULL");
                return false;
            }

            if ((_diagLogged & (1 << 3)) == 0)
            {
                LogDiag(1 << 3, $"[TerrainDiag] camera ok: {_mainCamera.name} at {_mainCamera.transform.position}");
            }

            return true;
        }

        private LightingEngine? ResolveLightingEngine()
        {
            LightingEngine? lightingEngine = _lightingEngine;
            if (lightingEngine == null)
            {
                if (!Application.isPlaying)
                {
                    return null;
                }

                throw new InvalidOperationException(
                    "LightingEngine was not initialized by GameLifetimeScope.");
            }

            return lightingEngine;
        }

        private void UpdateViewportDimensions(
            LightingEngine lightingEngine,
            out int requestedWidth,
            out int requestedHeight,
            out int effectiveViewportPadding,
            out bool dimensionsChanged)
        {
            Camera camera = _mainCamera!;
            effectiveViewportPadding = _viewportPadding;
            int requiredLightingPadding = lightingEngine.RequiredTerrainPadding +
                TerrainRegionAnchorCells + lightingEngine.StableRegionPaddingCells;
            effectiveViewportPadding = Mathf.Max(
                effectiveViewportPadding,
                requiredLightingPadding);

            requestedWidth = Mathf.Clamp(
                Mathf.CeilToInt((camera.orthographicSize * 2 * camera.aspect) / _cellSize) +
                    (effectiveViewportPadding * 2),
                2,
                MaximumTerrainDimension);
            requestedHeight = Mathf.Clamp(
                Mathf.CeilToInt((camera.orthographicSize * 2) / _cellSize) +
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

            dimensionsChanged = targetWidth != _meshWidth || targetHeight != _meshHeight;
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
        }

        private void ResolveGridPosition(
            int requestedWidth,
            int requestedHeight,
            int effectiveViewportPadding,
            bool dimensionsChanged,
            out Vector2Int currentGridPos,
            out int viewportMinX,
            out int viewportMinY,
            out int viewportWidth,
            out int viewportHeight)
        {
            Camera camera = _mainCamera!;
            Vector3 camPos = camera.transform.position;
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
            viewportWidth = Mathf.Max(2, requestedWidth - (effectiveViewportPadding * 2));
            viewportHeight = Mathf.Max(2, requestedHeight - (effectiveViewportPadding * 2));
            viewportMinX = Mathf.FloorToInt(camPos.x / _cellSize) - (viewportWidth / 2);
            viewportMinY = Mathf.FloorToInt(camPos.y / _cellSize) - (viewportHeight / 2);
            const int viewportMargin = 4;
            bool regionOutsideViewport =
                _lastGridPos.x == int.MinValue ||
                viewportMinX - viewportMargin < _lastGridPos.x ||
                viewportMinY - viewportMargin < _lastGridPos.y ||
                viewportMinX + viewportWidth + viewportMargin > _lastGridPos.x + _meshWidth ||
                viewportMinY + viewportHeight + viewportMargin > _lastGridPos.y + _meshHeight;

            currentGridPos = regionOutsideViewport || dimensionsChanged
                ? new Vector2Int(
                    SnapRegionCoordinate(desiredGridPos.x, regionAnchor),
                    SnapRegionCoordinate(desiredGridPos.y, regionAnchor))
                : _lastGridPos;

            if (currentGridPos.x == int.MinValue || currentGridPos.y == int.MinValue)
            {
                // Mesh rebuilds run every frame while the camera moves; a
                // persistent NaN camera must not repeat this warning per frame.
                if (!_invalidGridPositionLogged)
                {
                    _invalidGridPositionLogged = true;
                    Debug.LogWarning(
                        $"[TerrainRenderer] Invalid terrain grid position {currentGridPos}. " +
                        $"Camera position={camPos}; desired grid={desiredGridPos}; " +
                        $"last grid={_lastGridPos}; dimensions={_meshWidth}x{_meshHeight}.");
                }

                currentGridPos = new Vector2Int(
                    SnapRegionCoordinate(desiredGridPos.x, regionAnchor),
                    SnapRegionCoordinate(desiredGridPos.y, regionAnchor));
            }
        }

        private void CoalesceOversizedDirtyRects()
        {
            // Past a certain size the rect-based patch stops being a saving:
            // it visits the same cells the full path would, without the full
            // path's parallelism. Scattered chunks arriving across the viewport
            // union into exactly that, so hand those back to the full rebuild
            // instead of walking most of the grid serially.
            if (!_dirtyRects.IsEmpty && _meshWidth > 0 && _meshHeight > 0)
            {
                // The sum of the rectangles, not their bounding box: that is
                // the number of cells the patch will actually visit.
                if (_dirtyRects.TotalArea * 2 >= (long)_meshWidth * _meshHeight)
                {
                    _needsRefresh = true;
                    _dirtyRects.Clear();
                }
            }
        }

        private void RebuildOrPatchTerrain(Vector2Int currentGridPos, bool dimensionsChanged)
        {
            bool terrainWasRebuilt = (currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged) && !BypassCpuMeshRebuild;
            if (terrainWasRebuilt)
            {
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;

                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);

                // The lighting material field is rasterized from this mesh. A streamed
                // chunk can change occupancy at the cache edge without changing the
                // camera lighting region, so every successful mesh rebuild — including
                // ones triggered only by _needsRefresh — must publish a new geometry
                // revision for normal/AO caches as well.
                _lightingGeometryRevision++;

                _dirtyRects.Clear();
            }
            else if (!_dirtyRects.IsEmpty && !BypassCpuMeshRebuild)
            {
                UpdateDirtyCells(currentGridPos.x, currentGridPos.y);

                // Same guarantee the full path above documents: this path now
                // carries streamed chunks too, and a streamed chunk changes
                // occupancy at the cache edge without moving the camera
                // lighting region, so the normal/AO caches have to be told.
                _lightingGeometryRevision++;
                _dirtyRects.Clear();
            }
        }

        private void PublishLightingUpdate(
            LightingEngine lightingEngine,
            int viewportMinX,
            int viewportMinY,
            int viewportWidth,
            int viewportHeight)
        {
            // The lighting engine adds its own stable-region padding around
            // this visible rectangle. The terrain mesh was already enlarged
            // above by RequiredTerrainPadding + anchor + that same stable
            // padding, so passing the complete padded mesh here would apply the
            // margins two more times. Apart from wasting most of the cascade
            // atlas, that made a VeryLow one-texel-per-cell field impossible to
            // fit at ordinary camera sizes (the 192x128 mesh became 288x224).
            // Keep command-buffer execution outside the URP sprite pass: the
            // material-field draw changes render targets and matrices.
            if (_mainCamera != null &&
                _mainCamera.orthographic &&
                lightingEngine.ActiveLightingQuality != LightingQualityMode.Off)
            {
                lightingEngine.UpdateLighting(
                    viewportMinX,
                    viewportMinY,
                    viewportWidth,
                    viewportHeight,
                    _mainCamera,
                    _storage,
                    _mapManager,
                    this);
                ValidateLightingBinding();
            }
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

            // Growing must happen immediately: keeping the smaller mesh while
            // zooming out leaves the newly-visible edges of the screen
            // unrendered until the debounce below expires. Only shrinking is
            // debounced — zoom changes continuously, and shrinking immediately
            // at each 32-cell boundary would turn one zoom gesture into
            // several full CPU rebuilds for no visible benefit.
            if (requested > current)
            {
                return allocated;
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

            commandBuffer.BeginSample("Fodinae.Terrain.RenderMaterialFields");
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

            commandBuffer.EndSample("Fodinae.Terrain.RenderMaterialFields");
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            if ((_diagLogged & (1 << 4)) == 0)
            {
                LogDiag(1 << 4, $"[TerrainDiag] UpdateVertexAttributes min=({minX},{minY}) size={_meshWidth}x{_meshHeight}");
            }

            ITextureService textureService = _textureService ??
                throw new InvalidOperationException("TerrainRenderer requires ITextureService injection.");
            if (_mapManager == null || _storage == null)
            {
                if ((_diagLogged & (1 << 5)) == 0)
                {
                    LogDiag(1 << 5, $"[TerrainDiag] BAIL: textureService=ok mapManager={(_mapManager == null ? "NULL" : "ok")}");
                }

                return;
            }

            var atlases = textureService.GetAllAtlases();
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
                IClientConfigManager clientConfigManager = _clientConfigManager ??
                    throw new InvalidOperationException(
                        "TerrainRenderer requires IClientConfigManager injection.");
                ClientConfig clientConfig = clientConfigManager.Config ??
                    throw new InvalidOperationException(
                        "TerrainRenderer requires an initialized ClientConfig.");
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
                    Shader terrainShader = _terrainShader ??
                        throw new InvalidOperationException(
                            "Terrain shader was not initialized before atlas material creation.");
                    _materials[i] = new Material(terrainShader);
                    RequireShaderProperties(_materials[i]);
                    _materials[i].SetVector("_FlowScale", clientConfig.TerrainFlowScale);
                    _materials[i].SetFloat(
                        "_ShimmerSpeedScale",
                        clientConfig.TerrainShimmerSpeedScale);
                    _materials[i].SetFloat(
                        "_PulseSpeedScale",
                        clientConfig.TerrainPulseSpeedScale);
                    _materials[i].SetColor(
                        "_ShimmerColor",
                        clientConfig.TerrainShimmerColor);
                    _materials[i].SetColor("_DebugColor", clientConfig.TerrainDebugColor);
                    _materials[i].SetFloat(
                        "_DebugMode",
                        clientConfig.TerrainDebugMode ? 1f : 0f);
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

            textureService.FlushDirtyAtlases();

            try
            {
                int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
                int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
                bool canScrollCache =
                    !_needsRefresh &&
                    _cellCache.CacheMinX != int.MinValue &&
                    Mathf.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
                    Mathf.Abs(cacheDeltaY) < _cellCache.CacheHeight;
                FrameProfiler.TerrainRebuildCount++;
                long swCache = System.Diagnostics.Stopwatch.GetTimestamp();
                using (CacheMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _cellCache.ScrollAndFill(cacheDeltaX, cacheDeltaY, _storage, _mapManager, textureService, atlases);
                    }
                    else
                    {
                        FrameProfiler.TerrainFullPopulateCount++;
                        _cellCache.PopulateFull(minX, minY, _storage, _mapManager, textureService, atlases);
                    }
                }

                FrameProfiler.TerrainCacheTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swCache) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                using (PrecalculateMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _precalc.PrecalculateIncremental(_cellCache, _meshWidth, _meshHeight, cacheDeltaX, cacheDeltaY, _mapManager.WorldWidth, _mapManager.WorldHeight);
                    }
                    else
                    {
                        _precalc.PrecalculateFull(_cellCache, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight);
                    }
                }

                long swFlood = System.Diagnostics.Stopwatch.GetTimestamp();
                using (FloodFillMarker.Auto())
                {
                    // Always full, even when the cache scrolled.
                    //
                    // ComputeIncremental does not reproduce ComputeFull, so which
                    // one ran was visible: the same world cells came out looking
                    // different depending on the path walked to reach them, and
                    // the next full rebuild snapped them back. See the remarks on
                    // ComputeIncremental for the two reasons.
                    //
                    // The cost lands only when the terrain region recenters, not
                    // per frame, and it is reported as TerrainFloodFillTimeMs -
                    // so if this turns out to be expensive it can be optimized
                    // against a measurement rather than by guessing.
                    _backgroundFloodFill.ComputeFull(this);
                }

                FrameProfiler.TerrainFloodFillTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swFlood) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                long swMesh = System.Diagnostics.Stopwatch.GetTimestamp();
                using (MeshBuildMarker.Auto())
                {
                    _meshBuilder.BuildFull(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _subMeshIndices, _useColorLod, _mapManager, textureService);
                }

                FrameProfiler.TerrainMeshTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swMesh) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                if (_mesh != null)
                {
                    long swUpload = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (MeshUploadMarker.Auto())
                    {
                        if (_mesh.vertexCount != _meshBuilder.VertexBuffer.Length || _mesh.subMeshCount != atlases.Count)
                        {
                            // Counted because atlases.Count grows as cell textures
                            // stream in, and every growth drops the whole mesh -
                            // which is a frame with nothing drawn.
                            FrameProfiler.TerrainMeshClearCount++;
                            _mesh.Clear();
                            _mesh.subMeshCount = atlases.Count;
                            _mesh.SetVertexBufferParams(_meshBuilder.VertexBuffer.Length, VertexLayout);
                        }

                        _mesh.SetVertexBufferData(
                            _meshBuilder.VertexBuffer,
                            0,
                            0,
                            _meshBuilder.VertexBuffer.Length,
                            0,
                            UPLOAD_FLAGS);
                    }

                    FrameProfiler.TerrainGpuUploadTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swUpload) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

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
                        string diagnostic =
                            "[TerrainDiag] BuildFull: grid=(" +
                            $"{_lastGridPos.x},{_lastGridPos.y}) " +
                            $"world={_mapManager.WorldWidth}x{_mapManager.WorldHeight} " +
                            $"verts={_meshBuilder.VertexBuffer.Length} meshVerts={_mesh.vertexCount} " +
                            $"bounds={_mesh.bounds} transform={transform.position}";
                        LogDiag(
                            1 << 8,
                            diagnostic);
                    }

                    for (int i = 0; i < atlases.Count; i++)
                    {
                        var atlasTex = atlases[i].Texture;
                        if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                        {
                            _materials[i].SetTexture("_BaseMap", atlasTex);
                        }

                        if (_materials[i].GetTexture("_FlowMap") != textureService.FlowMapTexture)
                        {
                            _materials[i].SetTexture("_FlowMap", textureService.FlowMapTexture);
                        }

                        _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                    }
                }

                _needsRefresh = false;
            }
            catch (Exception ex)
            {
                _fatalBuildError = true;
                Debug.LogException(new InvalidOperationException(
                    $"[TerrainRenderer] Build failed: grid=({minX},{minY}) " +
                    $"size={_meshWidth}x{_meshHeight}, world=" +
                    $"{_mapManager?.WorldWidth ?? 0}x{_mapManager?.WorldHeight ?? 0}, " +
                    $"atlases={_textureService?.GetAllAtlases().Count ?? 0}, " +
                    $"storageReady={_storage?.IsReady ?? false}.",
                    ex));
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

        private static void RequireShaderProperties(Material material)
        {
            string[] requiredProperties =
            [
                "_BaseMap",
                "_FlowMap",
                "_FlowScale",
                "_ShimmerSpeedScale",
                "_PulseSpeedScale",
                "_ShimmerColor",
                "_DebugColor",
                "_DebugMode",
            ];
            foreach (string propertyName in requiredProperties)
            {
                if (!material.HasProperty(propertyName))
                {
                    throw new InvalidOperationException(
                        $"Terrain shader '{material.shader.name}' is missing required property " +
                        $"'{propertyName}'. Client graphics settings cannot be applied.");
                }
            }
        }

        private void UpdateDirtyCells(int minX, int minY)
        {
            if (_dirtyRects.IsEmpty)
            {
                return;
            }

            if (_storage == null || !_storage.IsReady || _mapManager == null || _mesh == null)
            {
                return;
            }

            ITextureService? textureService = _textureService ?? _subscribedTextureManager;
            if (textureService == null)
            {
                return;
            }

            var atlases = textureService.GetAllAtlases();
            if (atlases == null || atlases.Count == 0 || _subMeshIndices == null)
            {
                return;
            }

            FrameProfiler.TerrainDirtyPatchCount++;

            // Every pending rectangle is patched into the CPU buffers first,
            // and the buffers are uploaded once at the end. Uploading inside
            // the loop would cost one full vertex upload per rectangle - up to
            // DirtyRectSet.MaximumRects of them - which is more than the full rebuild
            // this path exists to avoid.
            bool anyIndicesChanged = false;
            for (int i = 0; i < _dirtyRects.Count; i++)
            {
                RectInt rect = _dirtyRects[i];

                // One cell of margin: a patched cell's appearance depends on
                // its neighbours' tiling and relief masks.
                int dirtyMinX = rect.xMin - 1;
                int dirtyMaxX = rect.xMax + 1;
                int dirtyMinY = rect.yMin - 1;
                int dirtyMaxY = rect.yMax + 1;

                int localStartX = dirtyMinX - minX;
                int localStartY = dirtyMinY - minY;
                int countX = dirtyMaxX - dirtyMinX;
                int countY = dirtyMaxY - dirtyMinY;

                _cellCache.UpdateRegion(dirtyMinX, dirtyMinY, countX, countY, _storage, _mapManager, textureService, atlases);
                _precalc.PrecalculateRegion(_cellCache, _meshWidth, _meshHeight, localStartX, localStartY, countX, countY, _mapManager.WorldWidth, _mapManager.WorldHeight);
                _backgroundFloodFill.UpdateLocalRegion(localStartX, localStartY, countX, countY, this);
                _meshBuilder.BuildRegion(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, localStartX, localStartY, countX, countY, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _subMeshIndices, _useColorLod, _mapManager, textureService);

                anyIndicesChanged |= _meshBuilder.IndicesChanged;
            }

            // One full upload, deliberately, even though BuildRegion reports the
            // exact span it wrote. Uploading only that span was measurably
            // worse, not better: this mesh is MarkDynamic, and a partial write
            // into a live dynamic vertex buffer makes the driver reconcile the
            // existing buffer contents instead of taking a whole new buffer,
            // which costs more than the bytes it saves. DirtyVertexStart /
            // DirtyVertexCount stay available for a caller that can use them.
            _mesh.SetVertexBufferData(
                _meshBuilder.VertexBuffer,
                0,
                0,
                _meshBuilder.VertexBuffer.Length,
                0,
                UPLOAD_FLAGS);

            // Indices only move when a quad changes atlas, which a patch almost
            // never does.
            if (anyIndicesChanged)
            {
                for (int i = 0; i < atlases.Count && i < _subMeshIndices.Length; i++)
                {
                    _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                }
            }
        }

        private void UpdateTextureCells(int minX, int minY)
        {
            if (_mapManager == null || _mesh == null || _textureService == null)
            {
                return;
            }

            IReadOnlyList<IAtlasDescriptor> atlases = _textureService.GetAllAtlases();
            if (atlases.Count == 0 || _subMeshIndices.Length == 0)
            {
                return;
            }

            _cellCache.RefreshTextureMetadata(
                _pendingTextureCellTypes,
                _mapManager,
                _textureService,
                atlases);
            _meshBuilder.BuildTextureCells(
                _pendingTextureCellTypes,
                _cellCache,
                _precalc,
                _backgroundFloodFill,
                minX,
                minY,
                _meshWidth,
                _meshHeight,
                _mapManager.WorldWidth,
                _mapManager.WorldHeight,
                atlases,
                _subMeshIndices,
                _useColorLod,
                _mapManager,
                _textureService);

            if (_meshBuilder.DirtyVertexCount == 0)
            {
                return;
            }

            _mesh.SetVertexBufferData(
                _meshBuilder.VertexBuffer,
                0,
                0,
                _meshBuilder.VertexBuffer.Length,
                0,
                UPLOAD_FLAGS);
            if (_meshBuilder.IndicesChanged)
            {
                for (int i = 0; i < atlases.Count && i < _subMeshIndices.Length; i++)
                {
                    _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                }
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
