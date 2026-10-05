#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

// Меш накладки дверей: те же адреса, что у меша идентификаторов, только
// дверных квадов и в слое 2 (адрес в сетке, без смещения видимого окна).
// Вид двери шейдер читает из буфера клеток, как у самого террейна; меш
// меняется, только когда меняется состав дверей или сдвигается окно.
public sealed class TerrainDoorOverlayRenderer : IDisposable
{
    private const MeshUpdateFlags UploadFlags =
        MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

    // Слой адреса накладки (TerrainCellData.hlsl).
    private const int OverlayLayer = 2;

    private readonly List<TerrainCellIdVertex> _vertices = [];
    private readonly List<int> _indices = [];
    private GameObject? _gameObject;
    private Mesh? _mesh;
    private MeshRenderer? _renderer;

    public void Rebuild(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        List<int> doorQuads,
        Material[] cellMaterials,
        string sortingLayerName,
        int sortingOrder,
        int meshWidth,
        int meshHeight,
        float cellSize)
    {
        EnsureObjects(parent, sceneObjects);
        if (_gameObject == null || _mesh == null || _renderer == null)
        {
            return;
        }

        if (doorQuads.Count == 0)
        {
            _gameObject.SetActive(false);
            return;
        }

        _gameObject.transform.localPosition = Vector3.zero;
        _gameObject.SetActive(true);
        _vertices.Clear();
        _indices.Clear();
        foreach (int quad in doorQuads)
        {
            int x = quad / meshHeight;
            int y = quad % meshHeight;
            int baseVertex = _vertices.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                _vertices.Add(new TerrainCellIdVertex(x, y, OverlayLayer, corner));
            }

            _indices.Add(baseVertex);
            _indices.Add(baseVertex + 1);
            _indices.Add(baseVertex + 2);
            _indices.Add(baseVertex);
            _indices.Add(baseVertex + 2);
            _indices.Add(baseVertex + 3);
        }

        // Переописывать буфер надо только когда изменилась его длина: состав
        // дверей в кадре почти всегда тот же самый.
        if (_mesh.vertexCount != _vertices.Count)
        {
            _mesh.Clear();
            _mesh.SetVertexBufferParams(_vertices.Count, TerrainCellIdMesh.VertexLayout);
        }

        _mesh.SetVertexBufferData(_vertices, 0, 0, _vertices.Count, 0, UploadFlags);
        // Индексы переписываются всегда: одна дверь сменилась другой — счёт
        // тот же, а треугольники другие.
        _mesh.SetIndices(_indices, MeshTopology.Triangles, 0, calculateBounds: false, baseVertex: 0);
        _mesh.bounds = new Bounds(
            new Vector3(meshWidth * cellSize * 0.5f, meshHeight * cellSize * 0.5f, 0f),
            new Vector3(
                (meshWidth * cellSize) + (cellSize * 2f),
                (meshHeight * cellSize) + (cellSize * 2f),
                2f));
        _renderer.sharedMaterials = cellMaterials;
        _renderer.sortingLayerName = sortingLayerName;
        _renderer.sortingOrder = sortingOrder;
    }

    public void Hide()
    {
        if (_gameObject != null)
        {
            _gameObject.SetActive(false);
        }
    }

    public void Dispose()
    {
        if (_gameObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_gameObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        _gameObject = null;
        _mesh = null;
        _renderer = null;
    }

    private void EnsureObjects(Transform parent, ISceneObjectFactory sceneObjects)
    {
        if (_gameObject != null)
        {
            return;
        }

        _gameObject = sceneObjects.Create("TerrainDoorOverlay");
        _gameObject.transform.SetParent(parent, worldPositionStays: false);
        var meshFilter = _gameObject.AddComponent<MeshFilter>();
        _renderer = _gameObject.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _mesh = new Mesh
        {
            name = "TerrainDoorOverlayMesh",
            indexFormat = IndexFormat.UInt32,
        };
        _mesh.MarkDynamic();
        meshFilter.sharedMesh = _mesh;
    }
}
