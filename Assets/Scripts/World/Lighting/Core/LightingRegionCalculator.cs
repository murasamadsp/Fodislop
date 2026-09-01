#nullable enable

using UnityEngine;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Computes quantized and padded camera regions for caching lighting computations.
    /// </summary>
    public static class LightingRegionCalculator
    {
        private const int LightingCacheAnchorCells = 8;
        private const int LightingRegionSizeQuantum = 32;
        public const int LightingRegionPaddingCells = 16;

        public static int SnapLightingRegion(int coordinate)
        {
            return Mathf.FloorToInt(coordinate / (float)LightingCacheAnchorCells) *
                LightingCacheAnchorCells;
        }

        public static Vector4 GetStableLightingRegion(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            Vector4 lastVisibleRegion)
        {
            int visibleMaxX = visibleMinX + visibleWidth;
            int visibleMaxY = visibleMinY + visibleHeight;

            if (!float.IsNaN(lastVisibleRegion.x))
            {
                int currentMinX = Mathf.RoundToInt(lastVisibleRegion.x);
                int currentMinY = Mathf.RoundToInt(lastVisibleRegion.y);
                int currentMaxX = currentMinX + Mathf.RoundToInt(lastVisibleRegion.z);
                int currentMaxY = currentMinY + Mathf.RoundToInt(lastVisibleRegion.w);
                int regionWidth = Mathf.RoundToInt(lastVisibleRegion.z);
                int regionHeight = Mathf.RoundToInt(lastVisibleRegion.w);
                int quarterRegionSize = Mathf.Min(regionWidth, regionHeight) / 4;
                int safeMargin = Mathf.Min(
                    LightingRegionPaddingCells,
                    Mathf.Max(2, quarterRegionSize));

                if (visibleMinX >= currentMinX + safeMargin &&
                    visibleMaxX <= currentMaxX - safeMargin &&
                    visibleMinY >= currentMinY + safeMargin &&
                    visibleMaxY <= currentMaxY - safeMargin)
                {
                    return lastVisibleRegion;
                }
            }

            int paddedMinX = SnapLightingRegion(visibleMinX - LightingRegionPaddingCells);
            int paddedMinY = SnapLightingRegion(visibleMinY - LightingRegionPaddingCells);
            int requiredWidth = visibleMaxX + LightingRegionPaddingCells - paddedMinX;
            int requiredHeight = visibleMaxY + LightingRegionPaddingCells - paddedMinY;
            int paddedWidth = Mathf.CeilToInt(
                requiredWidth / (float)LightingRegionSizeQuantum) *
                LightingRegionSizeQuantum;
            int paddedHeight = Mathf.CeilToInt(
                requiredHeight / (float)LightingRegionSizeQuantum) *
                LightingRegionSizeQuantum;

            return new Vector4(
                paddedMinX,
                paddedMinY,
                Mathf.Max(2, paddedWidth),
                Mathf.Max(2, paddedHeight));
        }
    }
}
