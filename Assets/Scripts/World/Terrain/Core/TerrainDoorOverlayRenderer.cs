#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Terrain;

/// <summary>
/// Renders only doorway terrain quads above world entities without duplicating
/// the complete terrain vertex buffer on the GPU.
/// </summary>
public sealed class TerrainDoorOverlayRenderer : IDisposable
{
    private const MeshUpdateFlags UploadFlags =
        MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

    private readonly List<TerrainVertex> _vertices = [];
    private List<int>[] _compactSubMeshIndices = Array.Empty<List<int>>();
    private GameObject? _gameObject;
    private Mesh? _mesh;
    private MeshRenderer? _renderer;

    public void Rebuild(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        TerrainMeshBuilder meshBuilder,
        List<int>[] sourceSubMeshIndices,
        Material[] materials,
        string sortingLayerName,
        int sortingOrder,
        int meshWidth,
        int meshHeight,
        float cellSize)
    {
        EnsureObjects(parent, sceneObjects);
        EnsureSubMeshLists(sourceSubMeshIndices.Length);
        _vertices.Clear();

        for (int atlasIndex = 0; atlasIndex < sourceSubMeshIndices.Length; atlasIndex++)
        {
            List<int> sourceIndices = sourceSubMeshIndices[atlasIndex];
            List<int> compactIndices = _compactSubMeshIndices[atlasIndex];
            compactIndices.Clear();
            for (int index = 0; index + 5 < sourceIndices.Count; index += 6)
            {
                int sourceVertex = sourceIndices[index];
                int compactVertex = _vertices.Count;
                _vertices.Add(meshBuilder.VertexBuffer[sourceVertex]);
                _vertices.Add(meshBuilder.VertexBuffer[sourceVertex + 1]);
                _vertices.Add(meshBuilder.VertexBuffer[sourceVertex + 2]);
                _vertices.Add(meshBuilder.VertexBuffer[sourceVertex + 3]);
                compactIndices.Add(compactVertex);
                compactIndices.Add(compactVertex + 3);
                compactIndices.Add(compactVertex + 2);
                compactIndices.Add(compactVertex + 2);
                compactIndices.Add(compactVertex + 1);
                compactIndices.Add(compactVertex);
            }
        }

        if (_gameObject == null || _mesh == null || _renderer == null)
        {
            return;
        }

        if (_vertices.Count == 0)
        {
            _gameObject.SetActive(false);
            return;
        }

        _gameObject.SetActive(true);
        _mesh.Clear();
        _mesh.SetVertexBufferParams(_vertices.Count, TerrainMeshManager.VertexLayout);
        _mesh.SetVertexBufferData(
            _vertices,
            0,
            0,
            _vertices.Count,
            0,
            UploadFlags);
        _mesh.subMeshCount = sourceSubMeshIndices.Length;
        for (int atlasIndex = 0; atlasIndex < _compactSubMeshIndices.Length; atlasIndex++)
        {
            _mesh.SetIndices(
                _compactSubMeshIndices[atlasIndex],
                MeshTopology.Triangles,
                atlasIndex,
                calculateBounds: false,
                baseVertex: 0);
        }

        _mesh.bounds = new Bounds(
            new Vector3(meshWidth * cellSize * 0.5f, meshHeight * cellSize * 0.5f, 0f),
            new Vector3(
                (meshWidth * cellSize) + (cellSize * 2f),
                (meshHeight * cellSize) + (cellSize * 2f),
                2f));
        _renderer.sharedMaterials = materials;
        _renderer.sortingLayerName = sortingLayerName;
        _renderer.sortingOrder = sortingOrder;
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

    private void EnsureSubMeshLists(int atlasCount)
    {
        if (_compactSubMeshIndices.Length == atlasCount)
        {
            return;
        }

        _compactSubMeshIndices = new List<int>[atlasCount];
        for (int atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
        {
            _compactSubMeshIndices[atlasIndex] = [];
        }
    }
}
