using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Color _shimmerHighlightColor = Color.white;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -1000;
        [SerializeField] private int _viewportPadding = 2;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Camera _mainCamera;

        private List<Vector3> _vertices = new();
        private List<Vector2> _uvs = new();
        private List<Color> _colors = new();
        private List<Vector4> _subAtlasRects = new();
        private List<Vector4> _tileSizeUVs = new();
        private List<Vector4> _worldPositions = new();
        private List<Vector4> _animationData = new();
        private List<Vector2> _shadowReliefData = new();
        private List<Vector2> _localUVs = new();

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private float _lastOrthoSize;
        private float _lastAspect;
        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;

        private void OnValidate()
        {
            if (!Application.isPlaying && _materials != null)
            {
                foreach (var mat in _materials)
                {
                    if (mat != null) mat.SetColor("_ShimmerColor", _shimmerHighlightColor);
                }
            }
        }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = Camera.main;

            _mesh = new Mesh();
            _mesh.name = "TerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            InitializeShader();

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
            gameObject.layer = 0;

            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
        }

        private void OnDestroy()
        {
            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded -= OnTextureLoaded;

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            CleanupMaterials();
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            _lastGridPos = new Vector2Int(int.MinValue, int.MinValue); // Force refresh
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
                if (_terrainShader == null)
                    _terrainShader = Resources.Load<Shader>("Shaders/Terrain");
            }
        }

        private void Update()
        {
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            bool viewportChanged = Mathf.Abs(_mainCamera.orthographicSize - _lastOrthoSize) > 0.01f ||
                                 Mathf.Abs(_mainCamera.aspect - _lastAspect) > 0.01f;

            if (viewportChanged || !_isInitialized)
            {
                _meshWidth = Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) + _viewportPadding * 2;
                _meshHeight = Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) + _viewportPadding * 2;

                _lastOrthoSize = _mainCamera.orthographicSize;
                _lastAspect = _mainCamera.aspect;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int currentGridPos = new Vector2Int(
                Mathf.FloorToInt(camPos.x / _cellSize) - _meshWidth / 2,
                Mathf.FloorToInt(camPos.y / _cellSize) - _meshHeight / 2
            );

            if (currentGridPos != _lastGridPos)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
            }
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            if (_subMeshIndices.Length != atlases.Count)
            {
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new();
                    _materials[i] = new Material(_terrainShader);
                }
            }

            foreach (var list in _subMeshIndices) list.Clear();

            _vertices.Clear();
            _uvs.Clear();
            _colors.Clear();
            _subAtlasRects.Clear();
            _tileSizeUVs.Clear();
            _worldPositions.Clear();
            _animationData.Clear();
            _shadowReliefData.Clear();
            _localUVs.Clear();

            int worldWidth = MapManager.Instance.WorldWidth;
            int worldHeight = MapManager.Instance.WorldHeight;

            ComputeBackgroundMap(minX, minY, minX + _meshWidth - 1, minY + _meshHeight - 1, out var bgMap);

            int vertexCount = 0;
            for (int y = 0; y < _meshHeight; y++)
            {
                for (int x = 0; x < _meshWidth; x++)
                {
                    int gridX = minX + x;
                    int unityY = minY + y;

                    int worldX = gridX % worldWidth; if (worldX < 0) worldX += worldWidth;
                    int serverY = worldHeight - 1 - unityY;
                    serverY = (serverY % worldHeight + worldHeight) % worldHeight;

                    CellType cellType = MapStorage.Instance.GetCell(worldX, serverY);
                    CellType bgType = bgMap[x, y];
                    bool hasBackground = bgType != cellType && bgType != 0;

                    // Background Quad
                    vertexCount = AddQuad(x, y, gridX, unityY, serverY, hasBackground ? bgType : CellType.Unloaded, 0.1f, vertexCount, atlases);

                    // Foreground Quad
                    vertexCount = AddQuad(x, y, gridX, unityY, serverY, cellType, 0.0f, vertexCount, atlases);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _subAtlasRects);
            _mesh.SetUVs(2, _tileSizeUVs);
            _mesh.SetUVs(3, _worldPositions);
            _mesh.SetUVs(4, _animationData);
            _mesh.SetUVs(5, _shadowReliefData);
            _mesh.SetUVs(6, _localUVs);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                var flowMapCoord = WorldTextureManager.Instance.GetFlowMapCoordinate(atlases[i]);
                Rect r = flowMapCoord.UVRect;
                _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);

                var atlasTex = atlases[i].Texture;
                if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                    _materials[i].SetTexture("_BaseMap", atlasTex);

                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i);
            }

            _mesh.RecalculateBounds();
            _mesh.UploadMeshData(false);
            _meshRenderer.sharedMaterials = _materials;
        }

        private int AddQuad(int localX, int localY, int gridX, int unityY, int serverY, CellType cellType, float zOffset, int vertexCount, List<TextureAtlas> atlases)
        {
            AtlasCoordinate coord = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, gridX, serverY);
            if (coord == AtlasCoordinate.Empty && cellType != CellType.Unloaded)
            {
                WorldTextureManager.Instance.RequestTexture(cellType);
            }

            int atlasIndex = 0;
            for (int i = 0; i < atlases.Count; i++)
            {
                if (atlases[i].ContainsCell(cellType))
                {
                    atlasIndex = i;
                    break;
                }
            }

            bool useFallback = coord == AtlasCoordinate.Empty;

            // Vertices
            _vertices.Add(new Vector3(localX * _cellSize, localY * _cellSize, zOffset) + GetVertexOffset(gridX, unityY));
            _vertices.Add(new Vector3((localX + 1) * _cellSize, localY * _cellSize, zOffset) + GetVertexOffset(gridX + 1, unityY));
            _vertices.Add(new Vector3((localX + 1) * _cellSize, (localY + 1) * _cellSize, zOffset) + GetVertexOffset(gridX + 1, unityY + 1));
            _vertices.Add(new Vector3(localX * _cellSize, (localY + 1) * _cellSize, zOffset) + GetVertexOffset(gridX, unityY + 1));

            // UVs with tiling support
            Vector2[] quadUVs = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
            int descriptor = 0;
            float isTiling = 0f;
            if (MapManager.Instance.TryGetTileGroup(cellType, out int groupId))
            {
                byte mask = GetNeighborMask(gridX, serverY, groupId);
                descriptor = TileBitmaskConverter.GetDescriptor(mask);
                isTiling = 1f;

                if ((descriptor & 0x40) != 0) { // flipX
                    (quadUVs[0].x, quadUVs[1].x) = (quadUVs[1].x, quadUVs[0].x);
                    (quadUVs[3].x, quadUVs[2].x) = (quadUVs[2].x, quadUVs[3].x);
                }
                if ((descriptor & 0x20) != 0) { // flipY
                    (quadUVs[0].y, quadUVs[3].y) = (quadUVs[3].y, quadUVs[0].y);
                    (quadUVs[1].y, quadUVs[2].y) = (quadUVs[2].y, quadUVs[1].y);
                }
                if ((descriptor & 0x80) != 0) { // rotate90
                    Vector2 temp = quadUVs[0]; quadUVs[0] = quadUVs[1]; quadUVs[1] = quadUVs[2]; quadUVs[2] = quadUVs[3]; quadUVs[3] = temp;
                }
            }
            _uvs.AddRange(quadUVs);

            // Colors
            Color mapColor = MapManager.Instance.GetCellMinimapColor(cellType);
            for (int i = 0; i < 4; i++) _colors.Add(useFallback ? mapColor : _shimmerHighlightColor);

            // Other attributes
            Vector4 frameRect = useFallback ? Vector4.zero : WorldTextureManager.Instance.GetCellFrameRect(cellType);
            float atlasSize = atlases[atlasIndex].Size;
            float uvTileSize = RenderingConstants.CELL_SIZE / atlasSize;

            var config = MapManager.Instance.GetCellConfig(cellType);
            float animType = useFallback ? 0f : (float)config.Animation;
            float speed = useFallback ? 0f : (float)config.AnimationSpeed;
            float offset = 0f;

            if (!useFallback && config.Animation == CellAnimationType.Blinking)
            {
                uint seed = (uint)(gridX * 374761397 + serverY * 668265263);
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                offset = (seed % 6283) / 1000f;
            }

            _subAtlasRects.AddRange(Enumerable.Repeat(frameRect, 4));
            _tileSizeUVs.AddRange(Enumerable.Repeat(new Vector4(uvTileSize, uvTileSize, 0, 0), 4));
            _worldPositions.AddRange(Enumerable.Repeat(new Vector4(gridX, serverY, descriptor & 0x1F, isTiling), 4));
            _animationData.AddRange(Enumerable.Repeat(new Vector4(animType, speed, offset, useFallback ? 1f : 0f), 4));

            float s0 = GetShadowValueForVertex(gridX, unityY);
            float s1 = GetShadowValueForVertex(gridX + 1, unityY);
            float s2 = GetShadowValueForVertex(gridX + 1, unityY + 1);
            float s3 = GetShadowValueForVertex(gridX, unityY + 1);
            float[] shadows = { s0, s1, s2, s3 };

            byte reliefGroup = GetReliefGroup(cellType);
            bool isRelief;
            byte reliefMask = GetReliefMask(gridX, serverY, reliefGroup, out isRelief);
            float textureType = isRelief ? 1.0f : 0.0f;

            for (int i = 0; i < 4; i++) _shadowReliefData.Add(new Vector2(textureType, isRelief ? reliefMask : shadows[i]));

            float r = 0.70710678f;
            _localUVs.Add(new(-r, -r)); _localUVs.Add(new(r, -r)); _localUVs.Add(new(r, r)); _localUVs.Add(new(-r, r));

            _subMeshIndices[atlasIndex].Add(vertexCount + 0);
            _subMeshIndices[atlasIndex].Add(vertexCount + 3);
            _subMeshIndices[atlasIndex].Add(vertexCount + 2);
            _subMeshIndices[atlasIndex].Add(vertexCount + 2);
            _subMeshIndices[atlasIndex].Add(vertexCount + 1);
            _subMeshIndices[atlasIndex].Add(vertexCount + 0);

            return vertexCount + 4;
        }

        private byte GetNeighborMask(int x, int serverY, int groupId)
        {
            byte mask = 0;
            int width = MapManager.Instance.WorldWidth;
            int height = MapManager.Instance.WorldHeight;
            int[] dx = { -1, -1, 0, 1, 1, 1, 0, -1 };
            int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = (x + dx[i] + width) % width;
                int ny = (serverY + dy[i] + height) % height;
                CellType neighborType = MapStorage.Instance.GetCell(nx, ny);
                if (MapManager.Instance.TryGetTileGroup(neighborType, out int neighborGroupId) && neighborGroupId == groupId)
                    mask |= (byte)(1 << i);
            }
            return mask;
        }

        private bool IsPassable(CellType cellType) => MapManager.Instance != null && (MapManager.Instance.GetCellConfig(cellType).Properties & CellConfigProperties.Passable) != 0;
        private struct TypeCount { public CellType type; public int count; }

        private void ComputeBackgroundMap(int minX, int minY, int maxX, int maxY, out CellType[,] bgMap)
        {
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            bgMap = new CellType[width, height];
            Queue<(int x, int y)> queue = new();

            int worldWidth = MapManager.Instance.WorldWidth;
            int worldHeight = MapManager.Instance.WorldHeight;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int gridX = minX + x;
                    int unityY = minY + y;
                    int worldX = gridX % worldWidth; if (worldX < 0) worldX += worldWidth;
                    int serverY = worldHeight - 1 - unityY;
                    serverY = (serverY % worldHeight + worldHeight) % worldHeight;

                    CellType cellType = MapStorage.Instance.GetCell(worldX, serverY);
                    if (IsPassable(cellType))
                    {
                        bgMap[x, y] = cellType;
                        queue.Enqueue((x, y));
                    }
                }
            }

            List<(int x, int y)> pass2Cells = new();
            Span<TypeCount> typeCounts = stackalloc TypeCount[8];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (bgMap[x, y] != 0) continue;
                    int distinctCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                            int gridX = minX + nx;
                            int unityY = minY + ny;
                            int worldX = gridX % worldWidth; if (worldX < 0) worldX += worldWidth;
                            int serverY = worldHeight - 1 - unityY;
                            serverY = (serverY % worldHeight + worldHeight) % worldHeight;

                            CellType nType = MapStorage.Instance.GetCell(worldX, serverY);
                            if (IsPassable(nType))
                            {
                                bool found = false;
                                for (int i = 0; i < distinctCount; i++)
                                {
                                    if (typeCounts[i].type == nType) { typeCounts[i].count++; found = true; break; }
                                }
                                if (!found && distinctCount < 8)
                                    typeCounts[distinctCount++] = new TypeCount { type = nType, count = 1 };
                            }
                        }
                    }

                    if (distinctCount > 0)
                    {
                        CellType mostFrequent = typeCounts[0].type;
                        int maxC = typeCounts[0].count;
                        for (int i = 1; i < distinctCount; i++)
                        {
                            if (typeCounts[i].count > maxC) { maxC = typeCounts[i].count; mostFrequent = typeCounts[i].type; }
                        }
                        bgMap[x, y] = mostFrequent;
                        pass2Cells.Add((x, y));
                    }
                }
            }
            foreach (var cell in pass2Cells) queue.Enqueue(cell);

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                CellType currentBg = bgMap[x, y];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        if (bgMap[nx, ny] == CellType.Unloaded) { bgMap[nx, ny] = currentBg; queue.Enqueue((nx, ny)); }
                    }
                }
            }

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (bgMap[x, y] == CellType.Unloaded) bgMap[x, y] = CellType.Empty;
        }

        private bool DropsShadow(CellType cellType) => MapManager.Instance != null && (MapManager.Instance.GetCellConfig(cellType).Properties & CellConfigProperties.DropsShadow) != 0;
        private bool ReceivesShadow(CellType cellType) => MapManager.Instance != null && (MapManager.Instance.GetCellConfig(cellType).Properties & CellConfigProperties.ReceivesShadow) != 0;
        private byte GetReliefGroup(CellType cellType) => MapManager.Instance != null ? MapManager.Instance.GetCellConfig(cellType).ReliefGroup : (byte)0;

        private float GetShadowValueForVertex(int x, int unityY)
        {
            int h = MapManager.Instance.WorldHeight;
            CellType tl = GetCellSafe(x - 1, h - 1 - unityY);
            CellType tr = GetCellSafe(x, h - 1 - unityY);
            CellType bl = GetCellSafe(x - 1, h - unityY);
            CellType br = GetCellSafe(x, h - unityY);
            return (DropsShadow(tl) || DropsShadow(tr) || DropsShadow(bl) || DropsShadow(br)) &&
                   (ReceivesShadow(tl) || ReceivesShadow(tr) || ReceivesShadow(bl) || ReceivesShadow(br)) ? 0.7f : 0.0f;
        }

        private CellType GetCellSafe(int x, int serverY)
        {
            int w = MapManager.Instance.WorldWidth, h = MapManager.Instance.WorldHeight;
            int wx = x % w; if (wx < 0) wx += w;
            int sy = serverY % h; if (sy < 0) sy += h;
            return MapStorage.Instance.GetCell(wx, sy);
        }

        private byte GetReliefMask(int x, int serverY, byte currentRelief, out bool isRelief)
        {
            isRelief = false;
            int w = MapManager.Instance.WorldWidth, h = MapManager.Instance.WorldHeight;
            byte mask = 0;
            if (GetReliefGroup(GetCellSafe(x, (serverY - 1 + h) % h)) >= currentRelief) mask += 1; else isRelief = true;
            if (GetReliefGroup(GetCellSafe((x - 1 + w) % w, serverY)) >= currentRelief) mask += 2; else isRelief = true;
            if (GetReliefGroup(GetCellSafe(x, (serverY + 1) % h)) >= currentRelief) mask += 4; else isRelief = true;
            if (GetReliefGroup(GetCellSafe((x + 1) % w, serverY)) >= currentRelief) mask += 8; else isRelief = true;
            return mask;
        }

        private Vector3 GetVertexOffset(int x, int unityY)
        {
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady) return Vector3.zero;
            int h = MapManager.Instance.WorldHeight;
            CellDistortionType tl = GetDistortion(x - 1, h - 1 - unityY);
            CellDistortionType tr = GetDistortion(x, h - 1 - unityY);
            CellDistortionType bl = GetDistortion(x - 1, h - unityY);
            CellDistortionType br = GetDistortion(x, h - unityY);
            if (tl == CellDistortionType.Block || tr == CellDistortionType.Block || bl == CellDistortionType.Block || br == CellDistortionType.Block) return Vector3.zero;
            int xSign = 0, ySign = 0;
            if (bl == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
            if (br == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
            if (tl == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
            if (tr == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }
            if (xSign == 0 && ySign == 0) return Vector3.zero;
            uint seed = (uint)(x * 374761397 + unityY * 668265263);
            seed = (seed ^ (seed >> 13)) * 1274126177;
            seed = seed ^ (seed >> 16);
            float rx = ((seed % 4) + 1) * 0.0625f;
            uint seed2 = seed * 2654435761u;
            float ry = ((seed2 % 4) + 1) * 0.0625f;
            return new Vector3(xSign > 0 ? rx : (xSign < 0 ? -rx : 0), ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
        }

        private CellDistortionType GetDistortion(int x, int serverY)
        {
            int w = MapManager.Instance.WorldWidth, h = MapManager.Instance.WorldHeight;
            int wx = x % w; if (wx < 0) wx += w;
            int sy = serverY % h; if (sy < 0) sy += h;
            CellType type = MapStorage.Instance.GetCell(wx, sy);
            if (type == CellType.Unloaded || type == CellType.Pregener) return CellDistortionType.Neutral;
            return MapManager.Instance.GetCellConfig(type).Distortion;
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
    }
}
