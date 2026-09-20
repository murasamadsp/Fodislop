#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Terrain;

// Canonical local geometry for one terrain cell. The GPU cell path rebuilds the
// same four corners from the cell geometry textures; CPU overlays use this
// value directly for POSITION. Keeping the corner order here prevents each
// path from inventing its own anchor convention.
public readonly record struct TerrainCellGeometry(
    Vector2 Corner00,
    Vector2 Corner10,
    Vector2 Corner11,
    Vector2 Corner01)
{
    public bool IsAnchored =>
        Corner00 != new Vector2(0f, 0f) ||
        Corner10 != new Vector2(1f, 0f) ||
        Corner11 != new Vector2(1f, 1f) ||
        Corner01 != new Vector2(0f, 1f);

    public static TerrainCellGeometry FromOffsets(
        Vector3 offset00,
        Vector3 offset10,
        Vector3 offset11,
        Vector3 offset01)
    {
        return new TerrainCellGeometry(
            new Vector2(offset00.x, offset00.y),
            new Vector2(1f + offset10.x, offset10.y),
            new Vector2(1f + offset11.x, 1f + offset11.y),
            new Vector2(offset01.x, 1f + offset01.y));
    }

    public Vector2 GetCorner(int corner)
    {
        return corner switch
        {
            0 => Corner00,
            1 => Corner10,
            2 => Corner11,
            3 => Corner01,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };
    }
}
