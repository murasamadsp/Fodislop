#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Lifecycle;
using Fodinae.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Game;

/// <summary>
/// Batches building roofs and badges that must render above terrain doorway overlays.
/// </summary>
public sealed class WorldEntityOverlayBatch : IDisposable
{
    private readonly Mesh _mesh;
    private Vector3[] _vertices = [];
    private Vector2[] _uvs = [];
    private Color32[] _colors = [];
    private int[] _indices = [];
    private int _uploadedSpriteCount = -1;

    public WorldEntityOverlayBatch(
        ISceneObjectFactory sceneObjects,
        Material material,
        int sortingOrder)
    {
        GameObject renderObject = sceneObjects.Create("WorldEntityOverlayBatch");
        _mesh = new Mesh
        {
            name = "WorldEntityOverlayBatch",
            indexFormat = IndexFormat.UInt32,
        };
        _mesh.MarkDynamic();
        var filter = renderObject.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;
        var renderer = renderObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
    }

    public void Rebuild(
        IReadOnlyList<WorldEntityBatchRenderer.SpriteHandle> sprites,
        Func<Texture2D, Rect> getAtlasRect)
    {
        int spriteCount = 0;
        for (int i = 0; i < sprites.Count; i++)
        {
            if (IsRenderable(sprites[i]))
            {
                spriteCount++;
            }
        }

        int vertexCount = spriteCount * 4;
        int indexCount = spriteCount * 6;
        EnsureCapacity(vertexCount, indexCount);
        int vertexCursor = 0;
        int indexCursor = 0;
        for (int i = 0; i < sprites.Count; i++)
        {
            WorldEntityBatchRenderer.SpriteHandle handle = sprites[i];
            if (!IsRenderable(handle))
            {
                continue;
            }

            WriteSprite(handle, getAtlasRect, vertexCursor, indexCursor);
            vertexCursor += 4;
            indexCursor += 6;
        }

        bool topologyChanged = _uploadedSpriteCount != spriteCount;
        if (topologyChanged)
        {
            _mesh.Clear(keepVertexLayout: true);
        }

        if (vertexCount > 0)
        {
            _mesh.SetVertices(_vertices, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetUVs(0, _uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetColors(_colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            if (topologyChanged)
            {
                _mesh.SetIndices(
                    _indices,
                    0,
                    indexCount,
                    MeshTopology.Triangles,
                    0,
                    calculateBounds: false);
            }

            _mesh.RecalculateBounds();
        }

        _uploadedSpriteCount = spriteCount;
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(_mesh);
    }

    private static bool IsRenderable(WorldEntityBatchRenderer.SpriteHandle handle)
    {
        return handle.Enabled &&
            handle.Transform != null &&
            handle.Sprite != null &&
            handle.SortingOrder >= RenderingConstants.BUILDING_ROOF_SORTING_ORDER;
    }

    private void WriteSprite(
        WorldEntityBatchRenderer.SpriteHandle handle,
        Func<Texture2D, Rect> getAtlasRect,
        int vertexOffset,
        int indexOffset)
    {
        Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
            "An enabled overlay sprite requires a Sprite.");
        Rect atlasRect = getAtlasRect(sprite.texture);
        Rect source = sprite.rect;
        Vector2 pivot = new(
            sprite.pivot.x / source.width,
            sprite.pivot.y / source.height);
        float width = source.width / sprite.pixelsPerUnit;
        float height = source.height / sprite.pixelsPerUnit;
        float left = -pivot.x * width;
        float bottom = -pivot.y * height;
        Transform spriteTransform = handle.Transform;

        _vertices[vertexOffset] = spriteTransform.TransformPoint(new Vector3(left, bottom, 0f));
        _vertices[vertexOffset + 1] = spriteTransform.TransformPoint(new Vector3(left, bottom + height, 0f));
        _vertices[vertexOffset + 2] = spriteTransform.TransformPoint(new Vector3(left + width, bottom, 0f));
        _vertices[vertexOffset + 3] = spriteTransform.TransformPoint(new Vector3(left + width, bottom + height, 0f));
        float uMin = atlasRect.xMin + ((source.xMin / sprite.texture.width) * atlasRect.width);
        float uMax = atlasRect.xMin + ((source.xMax / sprite.texture.width) * atlasRect.width);
        float vMin = atlasRect.yMin + ((source.yMin / sprite.texture.height) * atlasRect.height);
        float vMax = atlasRect.yMin + ((source.yMax / sprite.texture.height) * atlasRect.height);
        _uvs[vertexOffset] = new Vector2(uMin, vMin);
        _uvs[vertexOffset + 1] = new Vector2(uMin, vMax);
        _uvs[vertexOffset + 2] = new Vector2(uMax, vMin);
        _uvs[vertexOffset + 3] = new Vector2(uMax, vMax);
        for (int i = 0; i < 4; i++)
        {
            _colors[vertexOffset + i] = handle.Color;
        }

        _indices[indexOffset] = vertexOffset;
        _indices[indexOffset + 1] = vertexOffset + 1;
        _indices[indexOffset + 2] = vertexOffset + 2;
        _indices[indexOffset + 3] = vertexOffset + 2;
        _indices[indexOffset + 4] = vertexOffset + 1;
        _indices[indexOffset + 5] = vertexOffset + 3;
    }

    private void EnsureCapacity(int vertexCount, int indexCount)
    {
        if (_vertices.Length < vertexCount)
        {
            Array.Resize(ref _vertices, vertexCount);
            Array.Resize(ref _uvs, vertexCount);
            Array.Resize(ref _colors, vertexCount);
        }

        if (_indices.Length < indexCount)
        {
            Array.Resize(ref _indices, indexCount);
        }
    }
}
