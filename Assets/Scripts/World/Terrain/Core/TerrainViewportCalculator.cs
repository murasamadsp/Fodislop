#nullable enable

using UnityEngine;

namespace Fodinae.World.Terrain;

/// <summary>
/// Calculates dynamic terrain mesh dimensions, viewport sizing, padding, and region grid snapping.
/// </summary>
public sealed class TerrainViewportCalculator
{
    private const int TerrainRegionAnchorCells = 8;
    private const int DimensionAllocationQuantum = 32;
    private const int MaximumTerrainDimension = 384;
    private const float DimensionGrowDelay = 0.4f;
    private const int ViewportMargin = 4;

    private int _lastRequestedWidth;
    private int _lastRequestedHeight;
    private float _lastViewportSizeChangeTime;
    private bool _invalidGridPositionLogged;

    public void CalculateDimensions(
        Camera camera,
        float cellSize,
        int baseViewportPadding,
        int requiredLightingPadding,
        int stableRegionPadding,
        int currentMeshWidth,
        int currentMeshHeight,
        bool isInitialized,
        out int meshWidth,
        out int meshHeight,
        out int effectivePadding,
        out int requestedWidth,
        out int requestedHeight,
        out bool dimensionsChanged)
    {
        effectivePadding = Mathf.Max(
            baseViewportPadding,
            requiredLightingPadding + TerrainRegionAnchorCells + stableRegionPadding);

        requestedWidth = Mathf.Clamp(
            Mathf.CeilToInt((camera.orthographicSize * 2 * camera.aspect) / cellSize) + (effectivePadding * 2),
            2,
            MaximumTerrainDimension);
        requestedHeight = Mathf.Clamp(
            Mathf.CeilToInt((camera.orthographicSize * 2) / cellSize) + (effectivePadding * 2),
            2,
            MaximumTerrainDimension);

        if (requestedWidth != _lastRequestedWidth || requestedHeight != _lastRequestedHeight)
        {
            _lastRequestedWidth = requestedWidth;
            _lastRequestedHeight = requestedHeight;
            _lastViewportSizeChangeTime = Time.unscaledTime;
        }

        bool viewportSizeSettled =
            !Application.isPlaying ||
            Time.unscaledTime - _lastViewportSizeChangeTime >= DimensionGrowDelay;

        int targetWidth = SelectCachedDimension(requestedWidth, currentMeshWidth, isInitialized, viewportSizeSettled);
        int targetHeight = SelectCachedDimension(requestedHeight, currentMeshHeight, isInitialized, viewportSizeSettled);

        dimensionsChanged = targetWidth != currentMeshWidth || targetHeight != currentMeshHeight;
        meshWidth = targetWidth;
        meshHeight = targetHeight;
    }

    public Vector2Int ResolveGridPosition(
        Camera camera,
        float cellSize,
        int meshWidth,
        int meshHeight,
        int requestedWidth,
        int requestedHeight,
        int effectivePadding,
        bool dimensionsChanged,
        Vector2Int lastGridPos,
        out int viewportMinX,
        out int viewportMinY,
        out int viewportWidth,
        out int viewportHeight)
    {
        Vector3 camPos = camera.transform.position;
        Vector2Int desiredGridPos = new(
            Mathf.FloorToInt(camPos.x / cellSize) - (meshWidth / 2),
            Mathf.FloorToInt(camPos.y / cellSize) - (meshHeight / 2));

        int regionAnchor = Mathf.Clamp(
            TerrainRegionAnchorCells,
            1,
            Mathf.Max(1, effectivePadding));

        viewportWidth = Mathf.Max(2, requestedWidth - (effectivePadding * 2));
        viewportHeight = Mathf.Max(2, requestedHeight - (effectivePadding * 2));
        viewportMinX = Mathf.FloorToInt(camPos.x / cellSize) - (viewportWidth / 2);
        viewportMinY = Mathf.FloorToInt(camPos.y / cellSize) - (viewportHeight / 2);

        bool regionOutsideViewport =
            lastGridPos.x == int.MinValue ||
            viewportMinX - ViewportMargin < lastGridPos.x ||
            viewportMinY - ViewportMargin < lastGridPos.y ||
            viewportMinX + viewportWidth + ViewportMargin > lastGridPos.x + meshWidth ||
            viewportMinY + viewportHeight + ViewportMargin > lastGridPos.y + meshHeight;

        Vector2Int currentGridPos = regionOutsideViewport || dimensionsChanged
            ? new Vector2Int(
                SnapRegionCoordinate(desiredGridPos.x, regionAnchor),
                SnapRegionCoordinate(desiredGridPos.y, regionAnchor))
            : lastGridPos;

        if (currentGridPos.x == int.MinValue || currentGridPos.y == int.MinValue)
        {
            if (!_invalidGridPositionLogged)
            {
                _invalidGridPositionLogged = true;
                Debug.LogWarning(
                    $"[TerrainViewportCalculator] Invalid terrain grid position {currentGridPos}. " +
                    $"Camera position={camPos}; desired grid={desiredGridPos}; " +
                    $"last grid={lastGridPos}; dimensions={meshWidth}x{meshHeight}.");
            }

            currentGridPos = new Vector2Int(
                SnapRegionCoordinate(desiredGridPos.x, regionAnchor),
                SnapRegionCoordinate(desiredGridPos.y, regionAnchor));
        }

        return currentGridPos;
    }

    private static int SelectCachedDimension(
        int requestedDimension,
        int currentDimension,
        bool isInitialized,
        bool viewportSizeSettled)
    {
        int quantumDimension = Mathf.CeilToInt((float)requestedDimension / DimensionAllocationQuantum) *
            DimensionAllocationQuantum;
        quantumDimension = Mathf.Clamp(quantumDimension, 2, MaximumTerrainDimension);

        if (!isInitialized || currentDimension <= 0)
        {
            return quantumDimension;
        }

        if (requestedDimension > currentDimension)
        {
            return quantumDimension;
        }

        if (viewportSizeSettled &&
            requestedDimension + (DimensionAllocationQuantum * 2) <= currentDimension)
        {
            return quantumDimension;
        }

        return currentDimension;
    }

    private static int SnapRegionCoordinate(int coordinate, int quantum)
    {
        int snapped = Mathf.FloorToInt((float)coordinate / quantum) * quantum;
        return snapped == int.MinValue ? snapped + quantum : snapped;
    }
}
