#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.UI;

/// <summary>
/// Owns the transparent route-thread texture drawn over the world map. The map
/// itself is rendered incrementally; this overlay is redrawn in full whenever a
/// route is present so the remaining path never lags behind.
/// </summary>
internal sealed class WorldMapPathOverlay : IDisposable
{
    private static readonly Color32 s_pathColor = new(255, 214, 0, 255);
    private static readonly Color32 s_targetColor = new(255, 255, 255, 255);

    private Texture2D? _texture;
    private Color32[]? _pixels;
    private UnityEngine.UIElements.Image? _overlayImage;

    public void Ensure(WorldMapPanel panel, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_texture != null && _texture.width == width && _texture.height == height)
        {
            return;
        }

        DisposeTexture();
        _texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            width,
            height,
            "WorldMapPathTexture",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        _pixels = new Color32[checked(width * height)];
        _texture.SetPixelData(_pixels, 0);
        _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        DynamicAtlasConfigurator.RegisterRuntimeRedrawn(_texture);

        _overlayImage = panel.PathOverlay;
        if (_overlayImage != null)
        {
            _overlayImage.image = _texture;
        }
    }

    /// <summary>
    /// Repaints the remaining route (yellow cells, white target). Y is mirrored
    /// versus serverY: texture row 0 is the bottom of the map, matching the
    /// viewport renderer.
    /// </summary>
    public void Draw(
        IReadOnlyList<Vector2Int>? path,
        int startIndex,
        ILocalPlayer? player,
        float viewCenterX,
        float viewCenterY,
        float cellsPerPixel)
    {
        Texture2D? texture = _texture;
        Color32[]? colors = _pixels;
        if (texture == null || colors == null ||
            colors.Length != checked(texture.width * texture.height) ||
            cellsPerPixel <= 0f)
        {
            return;
        }

        int width = texture.width;
        int height = texture.height;
        Array.Clear(colors, 0, colors.Length);

        if (path != null && path.Count > 0)
        {
            int lastIndex = path.Count - 1;
            for (int i = Math.Max(0, startIndex); i <= lastIndex; i++)
            {
                Vector2Int cell = path[i];

                // Geometrically exact fill: only texels whose centres fall inside
                // the route cell are painted (inverse of the renderer projection).
                float pxLo = ((cell.x - viewCenterX) / cellsPerPixel) + (width * 0.5f) - 0.5f;
                float pxHi = ((cell.x + 1f - viewCenterX) / cellsPerPixel) + (width * 0.5f) - 0.5f;
                float pyHi = (height * 0.5f) - 0.5f - ((cell.y - viewCenterY) / cellsPerPixel);
                float pyLo = (height * 0.5f) - 0.5f - ((cell.y + 1f - viewCenterY) / cellsPerPixel);

                int x0 = Mathf.CeilToInt(pxLo);
                int x1 = Mathf.FloorToInt(pxHi);
                int y0 = Mathf.CeilToInt(pyLo);
                int y1 = Mathf.FloorToInt(pyHi);

                // Degenerate span: the cell border landed exactly on a texel centre.
                if (x1 < x0)
                {
                    x0 = x1 = Mathf.RoundToInt((pxLo + pxHi) * 0.5f);
                }

                if (y1 < y0)
                {
                    y0 = y1 = Mathf.RoundToInt((pyLo + pyHi) * 0.5f);
                }

                Color32 color = i == lastIndex ? s_targetColor : s_pathColor;
                for (int y = Mathf.Max(y0, 0); y <= Mathf.Min(y1, height - 1); y++)
                {
                    int rowStart = y * width;
                    for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, width - 1); x++)
                    {
                        colors[rowStart + x] = color;
                    }
                }
            }
        }

        // The robot cell stays transparent so the red map marker always wins.
        if (player is { HasServerPosition: true })
        {
            Vector2Int playerPos = player.Position;
            float pxLo = ((playerPos.x - viewCenterX) / cellsPerPixel) + (width * 0.5f) - 0.5f;
            float pxHi = ((playerPos.x + 1f - viewCenterX) / cellsPerPixel) + (width * 0.5f) - 0.5f;
            float pyHi = (height * 0.5f) - 0.5f - ((playerPos.y - viewCenterY) / cellsPerPixel);
            float pyLo = (height * 0.5f) - 0.5f - ((playerPos.y + 1f - viewCenterY) / cellsPerPixel);

            int x0 = Mathf.Max(0, Mathf.CeilToInt(pxLo));
            int x1 = Mathf.Min(width - 1, Mathf.FloorToInt(pxHi));
            int y0 = Mathf.Max(0, Mathf.CeilToInt(pyLo));
            int y1 = Mathf.Min(height - 1, Mathf.FloorToInt(pyHi));
            for (int y = y0; y <= y1; y++)
            {
                int rowStart = y * width;
                for (int x = x0; x <= x1; x++)
                {
                    colors[rowStart + x] = default;
                }
            }
        }

        texture.SetPixelData(colors, 0);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _overlayImage?.MarkDirtyRepaint();
    }

    public void Dispose() => DisposeTexture();

    private void DisposeTexture()
    {
        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }

        _pixels = null;
        _overlayImage = null;
    }
}
