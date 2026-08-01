#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.World.Lighting;
using UnityEngine;
using Unity.Profiling;
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
        private const float DimensionGrowDelay = 0.2f;

        public static TerrainRenderer? Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = GameConstants.World.CELLSIZE;
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
        private float _targetUseLight2D;
        private int _lastAtlasCount = -1;

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
        private const MeshUpdateFlags UPLOAD_FLAGS = MeshUpdateFlags.DontValidateIndices;
        private static readonly ProfilerMarker CacheMarker = new("Fodinae.Terrain.Cache");
        private static readonly ProfilerMarker PrecalculateMarker = new("Fodinae.Terrain.Precalculate");
        private static readonly ProfilerMarker FloodFillMarker = new("Fodinae.Terrain.BackgroundFloodFill");
        private static readonly ProfilerMarker MeshBuildMarker = new("Fodinae.Terrain.MeshBuild");
        private static readonly ProfilerMarker MeshUploadMarker = new("Fodinae.Terrain.MeshUpload");

        public CachedCellInfo GetCell(int x, int y)
        {
            var c = _cellCache.GetCellData(x, y);
            return new CachedCellInfo { Type = c.Type, Properties = c.Properties };
        }

        public static void OnCellChanged(int x, int y)
        {
            Instance?.HandleCellChanged(x, y);
        }

        private void HandleCellChanged(int serverX, int serverY)
        {
            if (_mapManager == null || _lastGridPos.x == int.MinValue)
            {
                _needsRefresh = true;
                return;
            }

            int unityY = Mathf.FloorToInt(CoordinateUtils.ServerToUnityY(serverY, _mapManager.WorldHeight));
            bool affectsCachedTerrain =
                serverX >= _lastGridPos.x - 1 &&
                serverX <= _lastGridPos.x + _meshWidth &&
                unityY >= _lastGridPos.y - 1 &&
                unityY <= _lastGridPos.y + _meshHeight;
            if (affectsCachedTerrain)
            {
                _needsRefresh = true;
            }

            TerrariaLightingEngine.Instance?.InvalidateCell(serverX, unityY);
        }

        protected void Awake()
        {
            if (Instance != null && Instance != this)
            {
                DestroyTerrainObject(gameObject);
                return;
            }

            Instance = this;
            _targetUseLight2D = PlayerPrefs.GetInt("UseLight2D", 1) == 1 ? 1f : 0f;
            InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

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

        public void EnsureSubscriptions()
        {
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
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain")
                                 ?? Resources.Load<Shader>("Shaders/Terrain")
                                 ?? Shader.Find("Universal Render Pipeline/Lit")
                                 ?? Shader.Find("Sprites/Default");
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
                TerrariaLightingEngine.Instance?.InvalidateStaticCache();
            }
        }

        private void OnWorldDataLoaded()
        {
            _needsRefresh = true;
            TerrariaLightingEngine.Instance?.InvalidateStaticCache();
        }

        protected void LateUpdate()
        {
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                if ((_diagLogged & (1 << 0)) == 0)
                {
                    LogDiag(1 << 0, $"[TerrainDiag] gate BLOCKED: mapManager={(_mapManager == null ? "NULL" : "ok")}, storage={(_storage == null ? "NULL" : (_storage.IsReady ? "ready" : "NOT_READY"))}");
                }
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
                _mainCamera = _mapManager.MainCamera;
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

            TerrariaLightingEngine? lightingEngine = null;
            int effectiveViewportPadding = _viewportPadding;
            if (_targetUseLight2D > 0.5f)
            {
                lightingEngine = TerrariaLightingEngine.Instance;
                if (lightingEngine == null)
                {
                    lightingEngine = gameObject.AddComponent<TerrariaLightingEngine>();
                }

                effectiveViewportPadding = Mathf.Max(
                    effectiveViewportPadding,
                    lightingEngine.RequiredTerrainPadding +
                    TerrainRegionAnchorCells +
                    lightingEngine.StableRegionPaddingCells);
            }

            int requestedWidth = Mathf.Clamp(
                Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) +
                    (effectiveViewportPadding * 2),
                2,
                256);
            int requestedHeight = Mathf.Clamp(
                Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) +
                    (effectiveViewportPadding * 2),
                2,
                256);
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
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
            }

            // A full terrain rebuild is the largest CPU burst in this frame.
            // Do not queue lighting setup immediately after it: both systems
            // otherwise compete for the same frame and the render thread waits
            // for CPU-side mesh/light data before the GPU receives work.
            if (_targetUseLight2D > 0.5f && !terrainWasRebuilt)
            {
                // Lighting follows the actual camera viewport, not the cached
                // terrain origin. The terrain mesh intentionally uses
                // hysteresis now, so feeding currentGridPos here would leave
                // the lightmap behind the camera while it moves inside the
                // cached mesh.
                int lightingPadding = lightingEngine!.RequiredTerrainPadding;
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
            }
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
            int allocated = Mathf.Min(256, allocationSteps * DimensionAllocationQuantum);
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

        public bool RenderLightingCoverage(RenderTexture target, Vector4 worldRect)
        {
            if (_mesh == null || _materials.Length == 0 || !target.IsCreated())
            {
                return false;
            }

            CommandBuffer commandBuffer = CommandBufferPool.Get("Fodinae Terrain Coverage");
            try
            {
                commandBuffer.SetRenderTarget(target);
                commandBuffer.ClearRenderTarget(clearDepth: false, clearColor: true, backgroundColor: Color.clear);

                Matrix4x4 view = Matrix4x4.identity;
                Matrix4x4 projection = Matrix4x4.Ortho(
                    worldRect.x,
                    worldRect.x + worldRect.z,
                    worldRect.y,
                    worldRect.y + worldRect.w,
                    -100f,
                    100f);
                commandBuffer.SetViewProjectionMatrices(
                    view,
                    GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));

                bool drewCoverage = false;
                int subMeshCount = Mathf.Min(_mesh.subMeshCount, _materials.Length);
                for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                {
                    Material material = _materials[subMeshIndex];
                    int coveragePass = material.FindPass("OcclusionCoverage");
                    if (coveragePass < 0)
                    {
                        continue;
                    }

                    commandBuffer.DrawMesh(
                        _mesh,
                        transform.localToWorldMatrix,
                        material,
                        subMeshIndex,
                        coveragePass);
                    drewCoverage = true;
                }

                Graphics.ExecuteCommandBuffer(commandBuffer);
                return drewCoverage;
            }
            finally
            {
                CommandBufferPool.Release(commandBuffer);
            }
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
                _cellCache.ClearCaches();
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                int estimatedPerAtlas = (_meshWidth * _meshHeight * 2 * 6 / atlases.Count) + 16;
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new List<int>(estimatedPerAtlas);
                    _materials[i] = new Material(_terrainShader);
                }

                materialsChanged = true;
            }
            else
            {
                int estimatedPerAtlas = (_meshWidth * _meshHeight * 2 * 6 / _subMeshIndices.Length) + 16;
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
                    _meshBuilder.BuildFull(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _subMeshIndices, _useColorLod);
                }

                if (_mesh != null)
                {
                    using (MeshUploadMarker.Auto())
                    {
                        _mesh.Clear();
                        _mesh.subMeshCount = atlases.Count;
                        _mesh.SetVertexBufferParams(_meshBuilder.VertexBuffer.Length, VertexLayout);
                        _mesh.SetVertexBufferData(_meshBuilder.VertexBuffer, 0, 0, _meshBuilder.VertexBuffer.Length, 0, UPLOAD_FLAGS);
                        // The terrain is a regular viewport-sized grid. Scanning
                        // every vertex after each rebuild is wasted CPU work and
                        // becomes noticeable with the 256x256 safety cap. Keep a
                        // conservative local-space bound that also contains the
                        // relief offsets and the two terrain layers.
                        _mesh.bounds = new Bounds(
                            new Vector3(_meshWidth * _cellSize * 0.5f, _meshHeight * _cellSize * 0.5f, 0f),
                            new Vector3(
                                _meshWidth * _cellSize + (_cellSize * 2f),
                                _meshHeight * _cellSize + (_cellSize * 2f),
                                2f));
                        if ((_diagLogged & (1 << 8)) == 0)
                        {
                            LogDiag(1 << 8, $"[TerrainDiag] BuildFull: verts={_meshBuilder.VertexBuffer.Length} meshVerts={_mesh.vertexCount} bounds={_mesh.bounds}");
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

                            _materials[i].SetFloat("_UseLight2D", _targetUseLight2D);

                            _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                        }
                    }
                }

                _needsRefresh = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TerrainRenderer] Build failed: {ex.Message}\n{ex.StackTrace}");
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

        public void SetUseLight2D(bool enabled)
        {
            _targetUseLight2D = enabled ? 1f : 0f;
            foreach (var mat in _materials)
            {
                if (mat != null)
                {
                    mat.SetFloat("_UseLight2D", _targetUseLight2D);
                }
            }

            PlayerPrefs.SetInt("UseLight2D", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
