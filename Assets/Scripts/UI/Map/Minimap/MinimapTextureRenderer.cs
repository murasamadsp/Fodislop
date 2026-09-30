#nullable enable

using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MinimapTextureRenderer
{
    private static readonly Color32 _OutOfBoundsColor = new(0, 0, 0, 255);
    private static readonly Color32 _CenterColor = Color.red;
    private static readonly Color32 _PathColor = new(255, 214, 0, 255);
    private static readonly Color32 _PathTargetColor = new(255, 255, 255, 255);

    private Color32[] _cellColors = new Color32[256];
    private Color32[]? _pixelColors;
    private readonly int _uiSize;

    public MinimapTextureRenderer(int uiSize)
    {
        _uiSize = uiSize;
        _pixelColors = new Color32[uiSize * uiSize];
    }

    public void CacheCellColors(MapManager mapManager) =>
        _cellColors = MapProjection.BuildCellColorTable(mapManager);

    public bool Render(
        Texture2D? texture,
        int playerX,
        int playerY,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        IReadOnlyList<Vector2Int>? clickPath,
        int pathStartIndex,
        bool drawPlayerMarker = true)
    {
        int texSize = _uiSize;
        Color32[]? colors = _pixelColors;
        if (colors == null)
        {
            return false;
        }

        int index = 0;
        bool hasLoadedCells = false;

        for (int texY = 0; texY < texSize; texY++)
        {
            int serverY = MapProjection.MinimapPixelToServerCell(0, texY, playerX, playerY, texSize).y;

            if (serverY < 0 || serverY >= worldHeight)
            {
                int end = index + texSize;
                while (index < end)
                {
                    colors[index++] = _OutOfBoundsColor;
                }

                continue;
            }

            for (int texX = 0; texX < texSize; texX++)
            {
                int serverX = MapProjection.MinimapPixelToServerCell(
                    texX,
                    texY,
                    playerX,
                    playerY,
                    texSize).x;

                colors[index++] = MapProjection.SampleCellColor(
                    cellSampler,
                    _cellColors,
                    serverX,
                    serverY,
                    worldWidth,
                    worldHeight,
                    _OutOfBoundsColor,
                    out bool loadedCell);
                hasLoadedCells |= loadedCell;
            }
        }

        // Нить клик-маршрута поверх клеток — сплошная, от следующего шага до
        // цели: жёлтые клетки остатка пути, цель — белая. Рисуется до маркера
        // игрока, чтобы робот был виден.
        if (clickPath != null)
        {
            for (int i = pathStartIndex; i < clickPath.Count; i++)
            {
                Vector2Int cell = clickPath[i];
                Vector2Int pixel = MapProjection.ServerCellToMinimapPixel(
                    cell.x,
                    cell.y,
                    playerX,
                    playerY,
                    texSize);
                if (pixel.x < 0 || pixel.y < 0 || pixel.x >= texSize || pixel.y >= texSize)
                {
                    continue;
                }

                colors[(pixel.y * texSize) + pixel.x] =
                    i == clickPath.Count - 1 ? _PathTargetColor : _PathColor;
            }
        }

        if (drawPlayerMarker)
        {
            Vector2Int marker = MapProjection.ServerCellToMinimapPixel(
                playerX,
                playerY,
                playerX,
                playerY,
                texSize);
            if (marker.x >= 0 && marker.y >= 0 && marker.x < texSize && marker.y < texSize)
            {
                // Маркер — ровно один пиксель, размером с блок миникарты.
                colors[(marker.y * texSize) + marker.x] = _CenterColor;
            }
        }

        if (texture != null)
        {
            texture.SetPixelData(colors, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        return hasLoadedCells;
    }
}
