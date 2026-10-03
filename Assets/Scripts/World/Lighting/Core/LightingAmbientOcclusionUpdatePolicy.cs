#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>Maps committed world-cell edits to AO render-target pixels.</summary>
/// <remarks>
/// A render target's row origin is device-dependent: Metal and Direct3D start
/// at the top, OpenGL at the bottom. Callers pass the field's row order from
/// <c>LightingFieldOrientation</c>, so the raster rectangle uses the same origin
/// as the scissored draw and the rasterized clear. A wrong origin mirrors the
/// region and the edited cells never leave the retained field.
/// </remarks>
internal static class LightingAmbientOcclusionUpdatePolicy
{
    // A cell edit changes its one-cell neighbourhood. Displaced geometry extends
    // less than half a cell and contact reach is 0.75 cells: ceil(1 + 0.5 + 0.75).
    // Keep the complete support, including old geometry that must be erased.
    internal const int SupportHaloCells = 3;

    public static bool CanUpdatePartially(
        LightingRuntimeState state,
        ulong terrainRevision,
        bool fieldWasDirty,
        bool resourcesChanged,
        bool regionChanged,
        bool contributorsChanged)
    {
        // Activating bounded changes sets FieldDirty too. Only a dirty field
        // from before activation means its retained contents cannot be reused.
        return !fieldWasDirty && !resourcesChanged && !regionChanged && !contributorsChanged &&
            state.StagedTerrainGeometryRevision == terrainRevision &&
            state.ActiveRegionInvalidations.Count > 0;
    }

    public static RectInt ResolveRasterRect(
        IReadOnlyList<RectInt> dirtyRegions,
        RectInt fieldWorldCells,
        int pixelsPerCell,
        bool rowsTopDown)
    {
        if (fieldWorldCells.width <= 0 || fieldWorldCells.height <= 0 || pixelsPerCell <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldWorldCells));
        }

        long minX = fieldWorldCells.xMax;
        long minY = fieldWorldCells.yMax;
        long maxX = fieldWorldCells.xMin;
        long maxY = fieldWorldCells.yMin;
        foreach (RectInt region in dirtyRegions)
        {
            if (region.width <= 0 || region.height <= 0)
            {
                throw new ArgumentException("AO changes must have positive extents.", nameof(dirtyRegions));
            }

            long left = Math.Max(fieldWorldCells.xMin, (long)region.xMin - SupportHaloCells);
            long bottom = Math.Max(fieldWorldCells.yMin, (long)region.yMin - SupportHaloCells);
            long right = Math.Min(fieldWorldCells.xMax, (long)region.x + region.width + SupportHaloCells);
            long top = Math.Min(fieldWorldCells.yMax, (long)region.y + region.height + SupportHaloCells);
            if (right <= left || top <= bottom)
            {
                continue;
            }

            minX = Math.Min(minX, left);
            minY = Math.Min(minY, bottom);
            maxX = Math.Max(maxX, right);
            maxY = Math.Max(maxY, top);
        }

        if (maxX <= minX || maxY <= minY)
        {
            return default;
        }

        int pixelWidth = checked((int)((maxX - minX) * pixelsPerCell));
        int pixelHeight = checked((int)((maxY - minY) * pixelsPerCell));
        int bottomUpY = checked((int)((minY - fieldWorldCells.yMin) * pixelsPerCell));
        // A top-down render target addresses bottom-up field row y as
        // height - 1 - y. Flip the rectangle so it matches the field transform
        // that wrote the geometry and the scissor that limits the redraw.
        int pixelY = rowsTopDown
            ? checked(fieldWorldCells.height * pixelsPerCell - bottomUpY - pixelHeight)
            : bottomUpY;
        return new RectInt(
            checked((int)((minX - fieldWorldCells.xMin) * pixelsPerCell)),
            pixelY,
            pixelWidth,
            pixelHeight);
    }
}
