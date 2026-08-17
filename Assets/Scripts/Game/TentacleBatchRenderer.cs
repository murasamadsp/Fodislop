#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Game
{
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

        private sealed class TextureChunk
        {
            public Mesh Mesh = null!;
            public readonly List<Tentacle> Tentacles = new();
            public Vector3[] Verts = new Vector3[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
            public Vector2[] Uvs = new Vector2[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
            public int[] Tris = new int[TRIS_PER_TENTACLE * INITIAL_CAPACITY];
            public bool GeometryDirty = true;
        }

        private readonly Dictionary<Texture2D, TextureChunk> _chunks = new();

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
                chunk.GeometryDirty = true;
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
                chunk.GeometryDirty = true;
            }
        }

        public void MarkDirty(Texture2D texture)
        {
            if (_chunks.TryGetValue(texture, out TextureChunk? chunk))
            {
                chunk.GeometryDirty = true;
            }
        }

        protected void LateUpdate()
        {
            foreach (TextureChunk chunk in _chunks.Values)
            {
                if (chunk.GeometryDirty)
                {
                    RebuildChunk(chunk);
                }
            }
        }

        private static void RebuildChunk(TextureChunk chunk)
        {
            int tentacleCount = 0;
            for (int i = 0; i < chunk.Tentacles.Count; i++)
            {
                if (chunk.Tentacles[i].IsActive)
                {
                    tentacleCount++;
                }
            }

            int vertexCount = tentacleCount * VERTS_PER_TENTACLE;
            int indexCount = tentacleCount * TRIS_PER_TENTACLE;
            int vertexCapacity = Mathf.Max(1, vertexCount);
            if (chunk.Verts.Length < vertexCapacity)
            {
                Array.Resize(ref chunk.Verts, vertexCapacity);
                Array.Resize(ref chunk.Uvs, vertexCapacity);
            }

            int indexCapacity = Mathf.Max(1, indexCount);
            if (chunk.Tris.Length < indexCapacity)
            {
                Array.Resize(ref chunk.Tris, indexCapacity);
            }

            int activeTentacleIndex = 0;
            for (int i = 0; i < chunk.Tentacles.Count; i++)
            {
                Tentacle tentacle = chunk.Tentacles[i];
                if (!tentacle.IsActive)
                {
                    continue;
                }

                int vertexOffset = activeTentacleIndex * VERTS_PER_TENTACLE;
                tentacle.WriteGeometry(chunk.Verts, chunk.Uvs, vertexOffset);
                int indexOffset = activeTentacleIndex * TRIS_PER_TENTACLE;
                for (int segment = 0; segment < TentacleBatchRenderer.POINT_COUNT - 1; segment++)
                {
                    int baseVertex = vertexOffset + (segment * 2);
                    int triangle = indexOffset + (segment * 6);
                    chunk.Tris[triangle] = baseVertex;
                    chunk.Tris[triangle + 1] = baseVertex + 1;
                    chunk.Tris[triangle + 2] = baseVertex + 2;
                    chunk.Tris[triangle + 3] = baseVertex + 2;
                    chunk.Tris[triangle + 4] = baseVertex + 1;
                    chunk.Tris[triangle + 5] = baseVertex + 3;
                }

                activeTentacleIndex++;
            }

            Mesh mesh = chunk.Mesh;
            mesh.Clear(keepVertexLayout: true);
            if (vertexCount > 0)
            {
                mesh.SetVertices(chunk.Verts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetUVs(0, chunk.Uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetIndices(chunk.Tris, 0, indexCount, MeshTopology.Triangles, 0, calculateBounds: false);

                Vector3 minimum = chunk.Verts[0];
                Vector3 maximum = minimum;
                for (int i = 1; i < vertexCount; i++)
                {
                    minimum = Vector3.Min(minimum, chunk.Verts[i]);
                    maximum = Vector3.Max(maximum, chunk.Verts[i]);
                }

                mesh.bounds = new Bounds(
                    (minimum + maximum) * 0.5f,
                    maximum - minimum + new Vector3(0.1f, 0.1f, 0.1f));
            }

            chunk.GeometryDirty = false;
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
}
