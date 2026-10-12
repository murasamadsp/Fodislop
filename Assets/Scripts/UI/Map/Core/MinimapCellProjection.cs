#nullable enable

using UnityEngine;

namespace Kern.UI;

internal static class MinimapCellProjection
{
    public static Vector2Int PixelToServerCell(
        int pixelX,
        int pixelY,
        int centerX,
        int centerY,
        int size)
    {
        int upperHalfSize = size / 2;
        int lowerHalfSize = (size - 1) / 2;
        return new Vector2Int(
            centerX - upperHalfSize + pixelX,
            centerY + lowerHalfSize - pixelY);
    }

    public static Vector2Int ServerCellToPixel(
        int serverX,
        int serverY,
        int centerX,
        int centerY,
        int size)
    {
        int upperHalfSize = size / 2;
        int lowerHalfSize = (size - 1) / 2;
        return new Vector2Int(
            serverX - centerX + upperHalfSize,
            centerY + lowerHalfSize - serverY);
    }
}
