using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
        [SerializeField] private int _bufferCells = 2;
        [SerializeField] private Shader _terrainShader;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;

        private List<Vector3> _vertices = new();
        private List<Vector2> _uvs = new(); // Quad-relative UVs [0,1]
        private List<Vector4> _subAtlasRects = new(); // [Umin, Vmin, WidthUV, HeightUV]
        private List<Vector4> _tileSizeUVs = new(); // [TileWidthUV, TileHeightUV, 0, 0]
        private List<Vector4> _worldPositions = new(); // [ServerX, ServerY, 0, 0]

        private Vector2Int _lastMinVisible = new Vector2Int(-1, -1);
        private Vector2Int _lastMaxVisible = new Vector2Int(-1, -1);
        private float _lastRebuildTime = 0;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh();
            _mesh.name = "TerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            InitializeShader();
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
                if (_terrainShader == null)
                {
                    _terrainShader = Resources.Load<Shader>("Shaders/Terrain");
                }
            }

            if (_terrainShader == null)
            {
                Debug.LogError("SingleMeshTerrainRenderer: Terrain shader NOT FOUND! Rendering will fail.");
            }
            else
            {
                Debug.Log($"SingleMeshTerrainRenderer: Using shader '{_terrainShader.name}'");
            }
        }

        private void LateUpdate()
        {
            if (Camera.main == null || MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
                return;

            UpdateVisibleMesh();
        }

        private void UpdateVisibleMesh()
        {
            Camera cam = Camera.main;
            float height = cam.orthographicSize * 2;
            float width = height * cam.aspect;
            Vector3 camPos = cam.transform.position;

            int minX = Mathf.FloorToInt((camPos.x - width / 2) / _cellSize) - _bufferCells;
            int maxX = Mathf.FloorToInt((camPos.x + width / 2) / _cellSize) + _bufferCells;
            int minY = Mathf.FloorToInt((camPos.y - height / 2) / _cellSize) - _bufferCells;
            int maxY = Mathf.FloorToInt((camPos.y + height / 2) / _cellSize) + _bufferCells;

            minX = Mathf.Max(0, minX);
            maxX = Mathf.Min(MapManager.Instance.WorldWidth - 1, maxX);
            minY = Mathf.Max(0, minY);
            maxY = Mathf.Min(MapManager.Instance.WorldHeight - 1, maxY);

            bool rangeChanged = minX != _lastMinVisible.x || minY != _lastMinVisible.y || maxX != _lastMaxVisible.x || maxY != _lastMaxVisible.y;
            bool timeToUpdateAnimations = Time.time - _lastRebuildTime > 0.05f;

            if (rangeChanged || timeToUpdateAnimations)
            {
                _lastMinVisible = new Vector2Int(minX, minY);
                _lastMaxVisible = new Vector2Int(maxX, maxY);
                _lastRebuildTime = Time.time;
                BuildMesh(minX, minY, maxX, maxY);
            }
        }

        private void BuildMesh(int minX, int minY, int maxX, int maxY)
        {
            _vertices.Clear();
            _uvs.Clear();
            _subAtlasRects.Clear();
            _tileSizeUVs.Clear();
            _worldPositions.Clear();

            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            if (_subMeshIndices.Length != atlases.Count)
            {
                _subMeshIndices = new List<int>[atlases.Count];
                for (int i = 0; i < atlases.Count; i++) _subMeshIndices[i] = new List<int>();

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

                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++)
                {
                    _materials[i] = new Material(_terrainShader);
                }
            }

            foreach (var list in _subMeshIndices) list.Clear();

            int vertexCount = 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int serverY = MapManager.Instance.WorldHeight - 1 - y;
                    CellType cellType = MapStorage.Instance.GetCell(x, serverY);

                    if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
                        continue;

                    int atlasIndex = -1;
                    for (int i = 0; i < atlases.Count; i++)
                    {
                        if (atlases[i].ContainsCell(cellType))
                        {
                            atlasIndex = i;
                            break;
                        }
                    }

                    if (atlasIndex == -1) continue;

                    var atlasTex = atlases[atlasIndex].Texture;
                    if (atlasTex == null) continue;

                    if (_materials[atlasIndex].GetTexture("_BaseMap") != atlasTex)
                    {
                        _materials[atlasIndex].SetTexture("_BaseMap", atlasTex);
                    }

                    _vertices.Add(new Vector3(x * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, 0));
                    _vertices.Add(new Vector3(x * _cellSize, (y + 1) * _cellSize, 0));

                    _uvs.Add(new Vector2(0, 0));
                    _uvs.Add(new Vector2(1, 0));
                    _uvs.Add(new Vector2(1, 1));
                    _uvs.Add(new Vector2(0, 1));

                    Vector4 frameRect = GetAnimationFrameRect(cellType, atlasIndex);
                    float tileSize = 32f;
                    float uSize = tileSize / atlases[atlasIndex].Size;
                    float vSize = tileSize / atlases[atlasIndex].Size;

                    for (int i = 0; i < 4; i++)
                    {
                        _subAtlasRects.Add(frameRect);
                        _tileSizeUVs.Add(new Vector4(uSize, vSize, 0, 0));
                        _worldPositions.Add(new Vector4(x, serverY, 0, 0));
                    }

                    _subMeshIndices[atlasIndex].Add(vertexCount + 0);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 3);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 2);

                    _subMeshIndices[atlasIndex].Add(vertexCount + 2);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 1);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 0);

                    vertexCount += 4;
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _subAtlasRects);
            _mesh.SetUVs(2, _tileSizeUVs);
            _mesh.SetUVs(3, _worldPositions);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i);
            }

            _mesh.RecalculateBounds();
            _meshRenderer.sharedMaterials = _materials;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }

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

        private Vector4 GetAnimationFrameRect(CellType cellType, int atlasIndex)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            var atlas = atlases[atlasIndex];

            AtlasCoordinate baseCoord = atlas.GetCoordinate(cellType);

            int frameIndex = 0;
            int frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);

            if (frameHeight > 0)
            {
                byte speed = MapManager.Instance.GetAnimationSpeed(cellType);
                if (speed == 0) speed = 5;

                int animationFrames = baseCoord.Height / frameHeight;
                if (animationFrames > 0)
                {
                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % animationFrames;
                }
            }

            float uMin = (float)baseCoord.AtlasX / atlas.Size;
            float vMin = (float)(baseCoord.AtlasY + frameIndex * (frameHeight > 0 ? frameHeight : 0)) / atlas.Size;
            float uSize = (float)baseCoord.Width / atlas.Size;
            float vSize = (float)(frameHeight > 0 ? frameHeight : baseCoord.Height) / atlas.Size;

            return new Vector4(uMin, vMin, uSize, vSize);
        }
    }
}
