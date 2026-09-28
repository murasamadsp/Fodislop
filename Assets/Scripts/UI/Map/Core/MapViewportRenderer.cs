#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MapViewportRenderer
{
    private readonly Color32 _defaultColor = new(0, 0, 0, 255);
    private readonly Color32[] _cellColorTable = new Color32[256];
    private Color32[]? _pixelBuffer;
    private Color32[]? _shiftBuffer;

    private bool _hasView;
    private int _lastTexWidth;
    private int _lastTexHeight;
    private int _lastWorldWidth;
    private int _lastWorldHeight;
    private float _lastCenterX;
    private float _lastCenterY;
    private float _lastCellsPerPixel;
    private float _pendingShiftX;
    private float _pendingShiftY;
    private MarkerRect _lastMarker;

    public Color32[] CellColorTable => _cellColorTable;

    public void InitColorTable(MapManager manager)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("[MapViewportRenderer] Cannot build color table: map manager is not initialized");
        }

        Color32[] colors = MapProjection.BuildCellColorTable(manager);
        Array.Copy(colors, _cellColorTable, colors.Length);
    }

    /// <summary>
    /// Сбрасывает опору инкрементальной отрисовки. Обязателен при пересоздании
    /// текстуры и при смене мира: старое содержимое буфера тогда описывает
    /// другую карту, и сдвигать его нельзя.
    /// </summary>
    public void InvalidateViewState()
    {
        _hasView = false;
        _pendingShiftX = 0f;
        _pendingShiftY = 0f;
        _lastMarker = MarkerRect.None;
    }

    public void Render(
        Texture2D? mapTexture,
        MapManager manager,
        MapCellSampler cellSampler,
        WorldMapMipCache? mipCache,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY,
        ILocalPlayer? player,
        bool playerBlinkState)
    {
        if (texWidth <= 0 || texHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texWidth), "Map texture dimensions must be positive.");
        }

        if (cellsPerPixel <= 0f || float.IsNaN(cellsPerPixel) || float.IsInfinity(cellsPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerPixel), "Map scale must be finite and positive.");
        }

        int texW = texWidth;
        int texH = texHeight;
        float cp = cellsPerPixel;

        EnsureBuffers(texW, texH);

        int worldW = manager.WorldWidth;
        int worldH = manager.WorldHeight;
        var context = new SampleContext(
            cellSampler,
            mipCache,
            texW,
            texH,
            cp,
            viewCenterX,
            viewCenterY,
            worldW,
            worldH,
            _defaultColor);

        MarkerRect marker = ComputeMarkerRect(player, playerBlinkState, context);

        bool rebuild = !_hasView ||
            texW != _lastTexWidth ||
            texH != _lastTexHeight ||
            worldW != _lastWorldWidth ||
            worldH != _lastWorldHeight ||
            !Mathf.Approximately(cp, _lastCellsPerPixel);

        if (rebuild)
        {
            _pendingShiftX = 0f;
            _pendingShiftY = 0f;
            RenderRegion(context, 0, texW - 1, 0, texH - 1);
            DrawMarker(context.TexWidth, marker);
        }
        else
        {
            int shiftX = AccumulateShiftX(viewCenterX, cp);
            int shiftY = AccumulateShiftY(viewCenterY, cp);

            if (Math.Abs(shiftX) >= texW || Math.Abs(shiftY) >= texH)
            {
                // Смещение шире текстуры: перекрытия не осталось, сдвигать нечего.
                _pendingShiftX = 0f;
                _pendingShiftY = 0f;
                RenderRegion(context, 0, texW - 1, 0, texH - 1);
                DrawMarker(context.TexWidth, marker);
            }
            else if (shiftX == 0 && shiftY == 0 && marker.Equals(_lastMarker))
            {
                StoreView(texW, texH, worldW, worldH, viewCenterX, viewCenterY, cp, marker);
                return;
            }

            ShiftContent(texW, texH, shiftX, shiftY);
            RenderExposedBands(context, shiftX, shiftY);
            RestoreMarkerArea(context, shiftX, shiftY, marker);
            DrawMarker(context.TexWidth, marker);
        }

        StoreView(texW, texH, worldW, worldH, viewCenterX, viewCenterY, cp, marker);

        if (mapTexture != null)
        {
            mapTexture.SetPixelData(_pixelBuffer, 0);
            mapTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
    }

    private void EnsureBuffers(int texWidth, int texHeight)
    {
        int length = texWidth * texHeight;
        if (_pixelBuffer == null || _pixelBuffer.Length != length)
        {
            _pixelBuffer = new Color32[length];
            _shiftBuffer = new Color32[length];
            InvalidateViewState();
        }
    }

    // Сдвиг накапливается дробью: округляя смещение каждый кадр, мы потеряли бы
    // до половины пикселя за кадр и панорама систематически отставала бы от курсора.
    private int AccumulateShiftX(float centerX, float cellsPerPixel)
    {
        _pendingShiftX += (_lastCenterX - centerX) / cellsPerPixel;
        int shift = Mathf.RoundToInt(_pendingShiftX);
        _pendingShiftX -= shift;
        return shift;
    }

    private int AccumulateShiftY(float centerY, float cellsPerPixel)
    {
        // Строка 0 текстуры — низ изображения, то есть максимальный server Y,
        // поэтому рост центра по Y двигает содержимое вверх по строкам.
        _pendingShiftY += (centerY - _lastCenterY) / cellsPerPixel;
        int shift = Mathf.RoundToInt(_pendingShiftY);
        _pendingShiftY -= shift;
        return shift;
    }

    private void ShiftContent(int texWidth, int texHeight, int shiftX, int shiftY)
    {
        if (shiftX == 0 && shiftY == 0)
        {
            return;
        }

        int texW = texWidth;
        int texH = texHeight;
        Color32[] source = _pixelBuffer!;
        Color32[] destination = _shiftBuffer!;

        int firstRow = Math.Max(0, shiftY);
        int lastRow = Math.Min(texH - 1, texH - 1 + shiftY);
        int firstColumn = Math.Max(0, shiftX);
        int lastColumn = Math.Min(texW - 1, texW - 1 + shiftX);
        for (int row = firstRow; row <= lastRow; row++)
        {
            Array.Copy(
                source,
                ((row - shiftY) * texW) - shiftX + firstColumn,
                destination,
                (row * texW) + firstColumn,
                lastColumn - firstColumn + 1);
        }

        Color32[] swap = _pixelBuffer!;
        _pixelBuffer = destination;
        _shiftBuffer = swap;
    }

    private void RenderExposedBands(SampleContext context, int shiftX, int shiftY)
    {
        int texW = context.TexWidth;
        int texH = context.TexHeight;

        if (shiftY > 0)
        {
            RenderRegion(context, 0, texW - 1, 0, shiftY - 1);
        }
        else if (shiftY < 0)
        {
            RenderRegion(context, 0, texW - 1, texH + shiftY, texH - 1);
        }

        int firstRow = Math.Max(0, shiftY);
        int lastRow = Math.Min(texH - 1, texH - 1 + shiftY);
        if (shiftX > 0)
        {
            RenderRegion(context, 0, shiftX - 1, firstRow, lastRow);
        }
        else if (shiftX < 0)
        {
            RenderRegion(context, texW + shiftX, texW - 1, firstRow, lastRow);
        }
    }

    private void RestoreMarkerArea(SampleContext context, int shiftX, int shiftY, MarkerRect marker)
    {
        MarkerRect stale = _lastMarker.Shifted(shiftX, shiftY);
        MarkerRect area = MarkerRect.Union(stale, marker, context.TexWidth, context.TexHeight);
        if (area.IsEmpty)
        {
            return;
        }

        RenderRegion(context, area.X0, area.X1, area.Y0, area.Y1);
    }

    private void DrawMarker(int texWidth, MarkerRect marker)
    {
        if (marker.IsEmpty)
        {
            return;
        }

        Color32 playerColor = new(255, 0, 0, 255);
        Color32[] buffer = _pixelBuffer!;
        int texW = texWidth;
        for (int row = marker.Y0; row <= marker.Y1; row++)
        {
            int rowStart = row * texW;
            for (int column = marker.X0; column <= marker.X1; column++)
            {
                buffer[rowStart + column] = playerColor;
            }
        }
    }

    private static MarkerRect ComputeMarkerRect(ILocalPlayer? player, bool playerBlinkState, SampleContext context)
    {
        if (player == null || !playerBlinkState)
        {
            return MarkerRect.None;
        }

        Vector2Int playerPos = player.Position;
        float cp = context.CellsPerPixel;
        float halfWidth = context.TexWidth * 0.5f * cp;
        float halfHeight = context.TexHeight * 0.5f * cp;
        float leftX = context.CenterX - halfWidth;
        float rightX = context.CenterX + halfWidth;
        float topServerY = context.CenterY - halfHeight;
        float bottomServerY = context.CenterY + halfHeight;

        bool visible = playerPos.x + 1f >= leftX && playerPos.x <= rightX &&
            playerPos.y + 1f >= topServerY && playerPos.y <= bottomServerY;
        if (!visible)
        {
            return MarkerRect.None;
        }

        Vector2 playerPixel = MapProjection.ServerCellToTexturePixel(
            playerPos.x,
            playerPos.y,
            context.CenterX,
            context.CenterY,
            cp,
            context.TexWidth,
            context.TexHeight);
        float markerSize = Mathf.Max(1f, 1f / cp);

        int x0 = Mathf.Clamp(Mathf.RoundToInt(playerPixel.x), 0, context.TexWidth - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(playerPixel.x + markerSize), 0, context.TexWidth - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(playerPixel.y), 0, context.TexHeight - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(playerPixel.y + markerSize), 0, context.TexHeight - 1);

        return new MarkerRect(x0, y0, x1, y1);
    }

    private void RenderRegion(SampleContext context, int pxStart, int pxEnd, int pyStart, int pyEnd)
    {
        if (pxStart > pxEnd || pyStart > pyEnd)
        {
            return;
        }

        Color32[] buffer = _pixelBuffer!;
        int texW = context.TexWidth;
        int texH = context.TexHeight;
        float cp = context.CellsPerPixel;
        float cx = context.CenterX;
        float cy = context.CenterY;
        int worldW = context.WorldWidth;
        int worldH = context.WorldHeight;

        // Полупиксельное смещение внутри texWidth - тот же отсчёт, что даёт
        // serverY = floor((cell + 0.5) - centerX) / cp, иначе сдвиг полосы
        // разошёлся бы с полной перерисовкой на доли клетки.
        float startWorldX = cx + (pxStart + 0.5f - texW * 0.5f) * cp;

        for (int py = pyStart; py <= pyEnd; py++)
        {
            int rowStart = py * texW;

            // Строка 0 текстуры — низ отображаемой карты. Серверные координаты
            // считаются сверху вниз, поэтому нижняя строка текстуры берёт
            // наибольший server Y во вьюпорте.
            float screenRowFromTop = texH - 0.5f - py;
            float worldY = cy + (screenRowFromTop - texH * 0.5f) * cp;
            int serverY = Mathf.FloorToInt(worldY);

            if (serverY < 0 || serverY >= worldH)
            {
                Array.Fill(buffer, context.OutOfBoundsColor, rowStart + pxStart, pxEnd - pxStart + 1);
                continue;
            }

            float worldX = startWorldX;
            for (int px = pxStart; px <= pxEnd; px++, worldX += cp)
            {
                int serverX = Mathf.FloorToInt(worldX);
                Color32 color;

                if (serverX < 0 || serverX >= worldW)
                {
                    color = context.OutOfBoundsColor;
                }
                else if (context.Mip != null && cp >= context.Mip.ChunkSize)
                {
                    color = context.Mip.Sample(worldX, worldY, cp);
                }
                else
                {
                    color = MapProjection.SampleCellColor(
                        context.Sampler,
                        _cellColorTable,
                        serverX,
                        serverY,
                        worldW,
                        worldH,
                        context.OutOfBoundsColor,
                        out _);
                }

                buffer[rowStart + px] = color;
            }
        }
    }

    private void StoreView(
        int texWidth,
        int texHeight,
        int worldWidth,
        int worldHeight,
        float viewCenterX,
        float viewCenterY,
        float cellsPerPixel,
        MarkerRect marker)
    {
        _hasView = true;
        _lastTexWidth = texWidth;
        _lastTexHeight = texHeight;
        _lastWorldWidth = worldWidth;
        _lastWorldHeight = worldHeight;
        _lastCenterX = viewCenterX;
        _lastCenterY = viewCenterY;
        _lastCellsPerPixel = cellsPerPixel;
        _lastMarker = marker;
    }

    private readonly struct SampleContext
    {
        public SampleContext(
            MapCellSampler sampler,
            WorldMapMipCache? mip,
            int texWidth,
            int texHeight,
            float cellsPerPixel,
            float centerX,
            float centerY,
            int worldWidth,
            int worldHeight,
            Color32 outOfBoundsColor)
        {
            Sampler = sampler;
            Mip = mip;
            TexWidth = texWidth;
            TexHeight = texHeight;
            CellsPerPixel = cellsPerPixel;
            CenterX = centerX;
            CenterY = centerY;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            OutOfBoundsColor = outOfBoundsColor;
        }

        public MapCellSampler Sampler { get; }

        public WorldMapMipCache? Mip { get; }

        public int TexWidth { get; }

        public int TexHeight { get; }

        public float CellsPerPixel { get; }

        public float CenterX { get; }

        public float CenterY { get; }

        public int WorldWidth { get; }

        public int WorldHeight { get; }

        public Color32 OutOfBoundsColor { get; }
    }

    internal readonly struct MarkerRect
    {
        public static MarkerRect None => default;

        public MarkerRect(int x0, int y0, int x1, int y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Valid = true;
        }

        public int X0 { get; }

        public int Y0 { get; }

        public int X1 { get; }

        public int Y1 { get; }

        public bool Valid { get; }

        public bool IsEmpty => !Valid;

        public MarkerRect Shifted(int shiftX, int shiftY) =>
            Valid ? new MarkerRect(X0 + shiftX, Y0 + shiftY, X1 + shiftX, Y1 + shiftY) : None;

        /// <summary>
        /// Прямоугольник маркера после смещения гарантированно лежит в буфере
        /// только в своей прежней позиции: сдвиг может увести его за любой край.
        /// Зажим обязателен до объединения — возвращать сырые координаты нельзя.
        /// </summary>
        private static MarkerRect ClampToBuffer(MarkerRect rect, int width, int height)
        {
            if (rect.IsEmpty)
            {
                return None;
            }

            if (rect.X0 > width - 1 || rect.X1 < 0 || rect.Y0 > height - 1 || rect.Y1 < 0)
            {
                return None;
            }

            int x0 = Math.Clamp(rect.X0, 0, width - 1);
            int y0 = Math.Clamp(rect.Y0, 0, height - 1);
            int x1 = Math.Clamp(rect.X1, 0, width - 1);
            int y1 = Math.Clamp(rect.Y1, 0, height - 1);
            return x1 < x0 || y1 < y0 ? None : new MarkerRect(x0, y0, x1, y1);
        }

        public static MarkerRect Union(MarkerRect first, MarkerRect second, int width, int height)
        {
            MarkerRect a = ClampToBuffer(first, width, height);
            MarkerRect b = ClampToBuffer(second, width, height);
            if (a.IsEmpty)
            {
                return b;
            }

            if (b.IsEmpty)
            {
                return a;
            }

            return new MarkerRect(
                Math.Min(a.X0, b.X0),
                Math.Min(a.Y0, b.Y0),
                Math.Max(a.X1, b.X1),
                Math.Max(a.Y1, b.Y1));
        }

        public bool Equals(MarkerRect other) =>
            Valid == other.Valid && (!Valid || (X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1));
    }
}
