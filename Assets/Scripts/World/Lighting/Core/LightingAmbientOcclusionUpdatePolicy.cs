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
    // less than half a cell and contact reach is 0.8 cells: ceil(1 + 0.5 + 0.8).
    // Keep the complete support, including old geometry that must be erased.
    internal const int SupportHaloCells = 3;
    internal const int MaximumPartialRasterRects = 8;

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

    public static IReadOnlyList<RectInt> ResolveRasterRects(
        IReadOnlyList<RectInt> dirtyRegions,
        RectInt fieldWorldCells,
        int pixelsPerCell,
        bool rowsTopDown)
    {
        if (fieldWorldCells.width <= 0 || fieldWorldCells.height <= 0 || pixelsPerCell <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldWorldCells));
        }

        var rasterRects = new List<RectInt>(Math.Min(dirtyRegions.Count, MaximumPartialRasterRects + 1));
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

            int pixelWidth = checked((int)((right - left) * pixelsPerCell));
            int pixelHeight = checked((int)((top - bottom) * pixelsPerCell));
            int bottomUpY = checked((int)((bottom - fieldWorldCells.yMin) * pixelsPerCell));
            // A top-down render target addresses bottom-up field row y as
            // height - 1 - y. Flip only the row of each support rectangle.
            int pixelY = rowsTopDown
                ? checked(fieldWorldCells.height * pixelsPerCell - bottomUpY - pixelHeight)
                : bottomUpY;
            rasterRects.Add(new RectInt(
                checked((int)((left - fieldWorldCells.xMin) * pixelsPerCell)),
                pixelY,
                pixelWidth,
                pixelHeight));
        }

        MergePixelFreeUnions(rasterRects);
        rasterRects.Sort(static (left, right) =>
        {
            int rowOrder = left.y.CompareTo(right.y);
            return rowOrder != 0 ? rowOrder : left.x.CompareTo(right.x);
        });

        if (rasterRects.Count > MaximumPartialRasterRects)
        {
            RectInt bounds = rasterRects[0];
            for (int i = 1; i < rasterRects.Count; i++)
            {
                bounds = Union(bounds, rasterRects[i]);
            }

            rasterRects.Clear();
            rasterRects.Add(bounds);
        }

        return rasterRects;
    }

    private static void MergePixelFreeUnions(List<RectInt> rects)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            bool merged;
            do
            {
                merged = false;
                for (int j = i + 1; j < rects.Count; j++)
                {
                    RectInt union = Union(rects[i], rects[j]);
                    long unionArea = (long)union.width * union.height;
                    long separateArea = ((long)rects[i].width * rects[i].height) +
                        ((long)rects[j].width * rects[j].height);
                    if (unionArea > separateArea)
                    {
                        continue;
                    }

                    rects[i] = union;
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
            while (merged);
        }
    }

    private static RectInt Union(RectInt left, RectInt right)
    {
        int minX = Math.Min(left.xMin, right.xMin);
        int minY = Math.Min(left.yMin, right.yMin);
        int maxX = Math.Max(left.xMax, right.xMax);
        int maxY = Math.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }
}
