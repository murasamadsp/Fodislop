#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Game;

/// <summary>
/// Batches every robot tail tentacle into a single mesh per tail texture:
/// one draw call for ALL tentacles renderer-wide, instead of 4 LineRenderer
/// draw calls per robot (LineRenderers never batch — MaterialPropertyBlock
/// makes each instance unique and Unity rebuilds their billboard geometry
/// on the CPU every frame).
/// </summary>
public class TentacleBatchRenderer : MonoBehaviour
{
    public const int POINT_COUNT = 5;
    private const int VERTS_PER_TENTACLE = POINT_COUNT * 2;
    private const int TRIS_PER_TENTACLE = (POINT_COUNT - 1) * 6;
    private const int INITIAL_CAPACITY = 64;
    private const int SORTING_ORDER = -1;
    private const float CULL_MARGIN = 3f;

    private sealed class TextureChunk
    {
        public Mesh Mesh = null!;
        public readonly List<Tentacle> Tentacles = new();
        public Vector3[] Verts = new Vector3[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        public Vector2[] Uvs = new Vector2[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        public int[] Tris = new int[TRIS_PER_TENTACLE * INITIAL_CAPACITY];
        public bool HasGeometry;
    }

    private readonly Dictionary<Texture2D, TextureChunk> _chunks = new();
    private Camera _camera = null!;

    public void Register(Tentacle tentacle, Texture2D texture)
    {
        if (tentacle == null || texture == null)
        {
            return;
        }

        if (!_chunks.TryGetValue(texture, out var chunk))
        {
            chunk = CreateChunk(texture);
            _chunks[texture] = chunk;
        }

        if (!chunk.Tentacles.Contains(tentacle))
        {
            chunk.Tentacles.Add(tentacle);
        }
    }

    public void Unregister(Tentacle tentacle, Texture2D texture)
    {
        if (tentacle == null || texture == null)
        {
            return;
        }

        if (_chunks.TryGetValue(texture, out var chunk))
        {
            chunk.Tentacles.Remove(tentacle);
        }
    }

    private TextureChunk CreateChunk(Texture2D texture)
    {
        var go = new GameObject($"TentacleChunk_{texture.name}");
        go.transform.SetParent(transform, false);

        var mesh = new Mesh { name = $"TentacleBatch_{texture.name}" };
        mesh.MarkDynamic();
        mesh.indexFormat = IndexFormat.UInt32;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = SharedMaterialCache.GetForTexture(texture);
        renderer.sortingOrder = SORTING_ORDER;

        return new TextureChunk { Mesh = mesh };
    }

    private void GetCameraRect(out Vector2 center, out Vector2 half)
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_camera == null || !_camera.orthographic)
        {
            center = Vector2.zero;
            half = new Vector2(float.MaxValue, float.MaxValue);
            return;
        }

        center = _camera.transform.position;
        float halfHeight = _camera.orthographicSize;
        half = new Vector2(halfHeight * _camera.aspect, halfHeight);
    }

    private static void EnsureCapacity(TextureChunk chunk, int tentacleCount)
    {
        int requiredVerts = tentacleCount * VERTS_PER_TENTACLE;
        if (chunk.Verts.Length >= requiredVerts)
        {
            return;
        }

        int newTentacles = chunk.Verts.Length / VERTS_PER_TENTACLE;
        while (newTentacles < tentacleCount)
        {
            newTentacles *= 2;
        }

        chunk.Verts = new Vector3[newTentacles * VERTS_PER_TENTACLE];
        chunk.Uvs = new Vector2[newTentacles * VERTS_PER_TENTACLE];
        chunk.Tris = new int[newTentacles * TRIS_PER_TENTACLE];
    }

    protected void OnDestroy()
    {
        foreach (var chunk in _chunks.Values)
        {
            if (chunk.Mesh != null)
            {
                Destroy(chunk.Mesh);
            }
        }

        _chunks.Clear();
    }
}
