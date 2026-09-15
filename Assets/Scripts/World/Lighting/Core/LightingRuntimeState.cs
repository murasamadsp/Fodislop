#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.World.Lighting;

internal sealed class LightingRuntimeState
{
    public bool FieldDirty { get; set; } = true;
    public bool CompositeDirty { get; set; } = true;
    public bool BounceDirty { get; set; } = true;
    public bool HasRenderedLightState { get; set; }
    public bool HasStaticRadianceState { get; set; }
    public bool HasDynamicRadianceState { get; set; }
    public bool WasLightingBypassed { get; set; }
    public bool DynamicSolveInProgress { get; set; }
    public Vector4 LastVisibleRegion { get; set; } = new(float.NaN, float.NaN, float.NaN, float.NaN);
    public ulong LastTerrainContentRevision { get; set; }
    public ulong LastContributorGeometryRevision { get; set; }
    public ulong SolveCount { get; set; }
    public float RequestedPixelsPerCell { get; set; }
    public float EffectivePixelsPerCell { get; set; }
    public bool TextureDimensionLimited { get; set; }
    public bool CascadeBudgetLimited { get; set; }

    // Geometry can arrive in the stable lighting padding before it is visible.
    // Keep that invalidation pending; activating it immediately would launch a
    // full static transport solve for pixels the player cannot see.
    private readonly List<RectInt> _pendingRegionInvalidations = new(8);
    private readonly List<RectInt> _activeRegionInvalidations = new(8);

    public IReadOnlyList<RectInt> ActiveRegionInvalidations => _activeRegionInvalidations;

    public void QueueRegionInvalidation(RectInt region)
    {
        if (region.width <= 0 || region.height <= 0)
        {
            return;
        }

        for (int index = 0; index < _pendingRegionInvalidations.Count; index++)
        {
            RectInt existing = _pendingRegionInvalidations[index];
            if (!existing.Equals(region))
            {
                continue;
            }

            return;
        }

        _pendingRegionInvalidations.Add(region);
    }

    public bool ActivatePendingRegionIfVisible(RectInt visibleRegion)
    {
        bool activated = false;
        for (int index = _pendingRegionInvalidations.Count - 1; index >= 0; index--)
        {
            RectInt pending = _pendingRegionInvalidations[index];
            if (!Intersects(pending, visibleRegion))
            {
                continue;
            }

            _pendingRegionInvalidations.RemoveAt(index);
            _activeRegionInvalidations.Add(pending);
            activated = true;
        }

        if (activated)
        {
            FieldDirty = true;
        }

        return activated;
    }

    public void ClearPendingRegionInvalidation()
    {
        _pendingRegionInvalidations.Clear();
        _activeRegionInvalidations.Clear();
    }

    public void CompleteActiveRegionInvalidation()
    {
        _activeRegionInvalidations.Clear();
    }

    private static bool Intersects(RectInt left, RectInt right)
    {
        return left.xMin < right.xMax &&
            left.xMax > right.xMin &&
            left.yMin < right.yMax &&
            left.yMax > right.yMin;
    }

}
