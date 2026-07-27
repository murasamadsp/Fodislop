using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using VContainer;

namespace Fodinae.Scripts.World.Terrain
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    [ExecuteAlways]
    public class TerrainRenderer : MonoBehaviour, ICachedCellDataProvider
    {
        public static TerrainRenderer Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private float _cellSize = GameConstants.World.CELLSIZE;
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Color _shimmerHighlightColor = Color.white;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -1000;
        [SerializeField] private int _viewportPadding = 2;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;

        [Inject] private IWorldDataStorage _storage = null!;
        [Inject] private MapManager _mapManager = null!;
        [Inject] private ITextureService _textureService = null!;

        private Mesh _mesh;
        private Camera _mainCamera;

        private TerrainCellCache _cellCache = new();
        private TerrainPrecalculator _precalc = new();
        private TerrainMeshBuilder _meshBuilder = new();
        private BackgroundFloodFill _backgroundFloodFill = new();

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;
        private bool _subscribedEvents = false;
        private bool _needsRefresh = false;
        private bool _useColorLod = false;
        private float _targetSimpleGraphics;
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

        public CachedCellInfo GetCell(int x, int y)
        {
            var c = _cellCache.GetCellData(x, y);
            return new CachedCellInfo { Type = c.Type, Properties = c.Properties };
        }

        public static void OnCellChanged(int x, int y)
        {
            if (Instance != null) Instance._needsRefresh = true;
        }

        protected void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            Instance = this;
            _targetSimpleGraphics = PlayerPrefs.GetInt("SimpleGraphics", 0) == 1 ? 1f : 0f;
            _targetUseLight2D = PlayerPrefs.GetInt("UseLight2D", 0) == 1 ? 1f : 0f;
            InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 };
                _mesh.MarkDynamic();
                _meshFilter.mesh = _mesh;
            }

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
        }

        public void EnsureSubscriptions()
        {
            if (_subscribedEvents) return;
            var wtm = _textureService as WorldTextureManager;
            if (wtm != null) wtm.OnTextureLoaded += OnTextureLoaded;
            if (_mapManager != null) _mapManager.OnWorldDataLoaded += OnWorldDataLoaded;
            _subscribedEvents = true;
        }

        protected void OnDestroy()
        {
            var wtm = _textureService as WorldTextureManager;
            if (wtm != null) wtm.OnTextureLoaded -= OnTextureLoaded;
            if (_mapManager != null) _mapManager.OnWorldDataLoaded -= OnWorldDataLoaded;

            if (Instance == this) Instance = null;
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
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
            Shader.SetGlobalFloat("_DarknessFactor", GameConstants.World.WORLD_DARKNESS_FACTOR);
            Shader.SetGlobalVector("_HeadlightPos", Vector4.zero);
            Shader.SetGlobalVector("_HeadlightDir", new Vector4(0, -1, 0, 0));
            Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            if (filename.StartsWith("Cells/", StringComparison.OrdinalIgnoreCase))
            {
                InitializeShader();
                _cellCache.ClearCaches();
                _needsRefresh = true;
            }
        }

        private void OnWorldDataLoaded()
        {
            _needsRefresh = true;
        }

        protected void LateUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) _storage?.EnsureEditorInitialized();
#endif
            if (_mapManager == null || _storage == null || !_storage.IsReady) return;

            if (_meshFilter != null && _meshFilter.sharedMesh != _mesh) _meshFilter.sharedMesh = _mesh;

            if (_mainCamera == null) _mainCamera = _mapManager.MainCamera;
            if (_mainCamera == null) return;

            int targetWidth = Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) + (_viewportPadding * 2);
            int targetHeight = Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) + (_viewportPadding * 2);

            targetWidth = Mathf.Clamp(targetWidth, 2, 256);
            targetHeight = Mathf.Clamp(targetHeight, 2, 256);
            if (targetWidth % 2 != 0) targetWidth++;
            if (targetHeight % 2 != 0) targetHeight++;

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
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int currentGridPos = new Vector2Int(
                Mathf.FloorToInt(camPos.x / _cellSize) - (_meshWidth / 2),
                Mathf.FloorToInt(camPos.y / _cellSize) - (_meshHeight / 2));

            if (currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
            }
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            var wtm = _textureService as WorldTextureManager;
            if (wtm == null || _mapManager == null) return;

            var atlases = wtm.GetAllAtlases();
            if (atlases == null || atlases.Count == 0) return;

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
                    if (list.Capacity < estimatedPerAtlas) list.Capacity = estimatedPerAtlas;
                }
            }

            wtm.FlushDirtyAtlases();

            bool canScroll = _cellCache.CacheMinX != int.MinValue && !_needsRefresh;
            int dx = 0, dy = 0;
            if (canScroll)
            {
                dx = (minX - 1) - _cellCache.CacheMinX;
                dy = (minY - 1) - _cellCache.CacheMinY;
                canScroll = Mathf.Abs(dx) < _cellCache.CacheWidth && Mathf.Abs(dy) < _cellCache.CacheHeight;
            }

            try
            {
                if (canScroll)
                {
                    _cellCache.ScrollAndFill(dx, dy, _storage, _mapManager, wtm, atlases);
                    _precalc.PrecalculateIncremental(_cellCache, _meshWidth, _meshHeight, dx, dy, _mapManager);
                    _backgroundFloodFill.ComputeIncremental(dx, dy, this);
                    _meshBuilder.BuildIncremental(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, dx, dy, atlases, _subMeshIndices, _useColorLod);

                    _mesh.SetVertexBufferData(_meshBuilder.VertexBuffer, 0, 0, _meshBuilder.VertexBuffer.Length, 0, UPLOAD_FLAGS | MeshUpdateFlags.DontRecalculateBounds);
                }
                else
                {
                    _cellCache.PopulateFull(minX, minY, _storage, _mapManager, wtm, atlases);
                    _precalc.PrecalculateFull(_cellCache, _meshWidth, _meshHeight, _mapManager);
                    _backgroundFloodFill.ComputeFull(this);
                    _meshBuilder.BuildFull(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _subMeshIndices, _useColorLod);

                    _mesh.Clear();
                    _mesh.subMeshCount = atlases.Count;
                    _mesh.SetVertexBufferParams(_meshBuilder.VertexBuffer.Length, VertexLayout);
                    _mesh.SetVertexBufferData(_meshBuilder.VertexBuffer, 0, 0, _meshBuilder.VertexBuffer.Length, 0, UPLOAD_FLAGS);
                    _mesh.RecalculateBounds();

                    for (int i = 0; i < atlases.Count; i++)
                    {
                        var atlasTex = atlases[i].Texture;
                        if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                        {
                            var flowMapCoord = wtm.GetFlowMapCoordinate(atlases[i]);
                            Rect r = flowMapCoord.UVRect;
                            _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                            _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                            _materials[i].SetTexture("_BaseMap", atlasTex);
                            _materials[i].SetFloat("_SimpleGraphics", _targetSimpleGraphics);
                            _materials[i].SetFloat("_UseLight2D", _targetUseLight2D);
                        }
                        _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
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
            if (!needReassignMaterials)
            {
                var sharedMats = _meshRenderer.sharedMaterials;
                if (sharedMats == null || sharedMats.Length != _materials.Length) needReassignMaterials = true;
                else
                {
                    for (int i = 0; i < _materials.Length; i++)
                        if (sharedMats[i] != _materials[i]) { needReassignMaterials = true; break; }
                }
            }

            if (needReassignMaterials) _meshRenderer.sharedMaterials = _materials;
        }

        private void CleanupMaterials()
        {
            if (_materials != null)
            {
                foreach (var mat in _materials)
                {
                    if (mat != null)
                    {
                        if (Application.isPlaying) Destroy(mat);
                        else DestroyImmediate(mat);
                    }
                }
            }
        }

        public void SetSimpleGraphics(bool enabled)
        {
            _targetSimpleGraphics = enabled ? 1f : 0f;
            foreach (var mat in _materials)
            {
                if (mat != null) mat.SetFloat("_SimpleGraphics", _targetSimpleGraphics);
            }
            PlayerPrefs.SetInt("SimpleGraphics", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetUseLight2D(bool enabled)
        {
            _targetUseLight2D = enabled ? 1f : 0f;
            foreach (var mat in _materials)
            {
                if (mat != null) mat.SetFloat("_UseLight2D", _targetUseLight2D);
            }
            PlayerPrefs.SetInt("UseLight2D", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
