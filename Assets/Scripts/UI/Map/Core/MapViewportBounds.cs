#nullable enable

using UnityEngine;

namespace Kern.UI;

public static class MapViewportBounds
{
    private const int MaxMapWidth = 960;
    private const int MaxMapHeight = 540;

    public static void CalculateTextureDimensions(
        float panelWidth,
        float panelHeight,
        out int texWidth,
        out int texHeight)
    {
        int width = panelWidth > 0f ? Mathf.RoundToInt(panelWidth) : 1920;
        int height = panelHeight > 0f ? Mathf.RoundToInt(panelHeight) : 1080;

        float aspect = (float)width / Mathf.Max(1, height);
        int targetWidth = MaxMapWidth;
        int targetHeight = Mathf.RoundToInt(targetWidth / aspect);
        if (targetHeight > MaxMapHeight)
        {
            targetHeight = MaxMapHeight;
            targetWidth = Mathf.RoundToInt(targetHeight * aspect);
        }

        texWidth = Mathf.Max(16, targetWidth);
        texHeight = Mathf.Max(16, targetHeight);
    }

    public static float ComputeMaxZoomOut(
        int texWidth,
        int texHeight,
        int worldWidth,
        int worldHeight)
    {
        if (texWidth <= 0 || texHeight <= 0 || worldWidth <= 0 || worldHeight <= 0)
        {
            return 16f;
        }

        float fitWorld = Mathf.Max((float)worldWidth / texWidth, (float)worldHeight / texHeight);
        return Mathf.Clamp(fitWorld * 1.5f, 4f, 32f);
    }

    public static void ClampViewCenter(
        ref float viewCenterX,
        ref float viewCenterY,
        float cellsPerPixel,
        int texWidth,
        int texHeight,
        int worldWidth,
        int worldHeight)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || texWidth <= 0 || texHeight <= 0)
        {
            return;
        }

        float halfWidth = texWidth * 0.5f * cellsPerPixel;
        float halfHeight = texHeight * 0.5f * cellsPerPixel;
        viewCenterX = ClampCenter(viewCenterX, halfWidth, worldWidth);
        viewCenterY = ClampCenter(viewCenterY, halfHeight, worldHeight);
    }

    public static float ClampCenter(float center, float halfViewport, int worldSize)
    {
        if (worldSize <= 0)
        {
            return center;
        }

        float min = -halfViewport;
        float max = worldSize + halfViewport;
        return Mathf.Clamp(center, min, max);
    }
}
