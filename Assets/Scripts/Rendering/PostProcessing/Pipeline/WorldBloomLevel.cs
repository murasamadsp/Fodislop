#nullable enable

using System;

namespace Kern.Rendering.PostProcessing;

/// <summary>Absolute world-pixel addressing for a small bloom target; never camera-relative phase.</summary>
internal readonly struct WorldBloomLevel
{
    public WorldBloomLevel(int minX, int minY, int width, int height, int stride)
    {
        MinX = minX;
        MinY = minY;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public int MinX { get; }
    public int MinY { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public static WorldBloomLevel Cover(int worldMinX, int worldMinY, int worldWidth, int worldHeight, int stride)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldWidth));
        }

        int minX = checked((int)Math.Floor((double)worldMinX / stride));
        int minY = checked((int)Math.Floor((double)worldMinY / stride));
        int endX = checked((int)Math.Ceiling(((double)worldMinX + worldWidth) / stride));
        int endY = checked((int)Math.Ceiling(((double)worldMinY + worldHeight) / stride));
        return new WorldBloomLevel(checked(minX * stride), checked(minY * stride),
            checked(endX - minX), checked(endY - minY), stride);
    }

    public (double X, double Y, double OffsetX, double OffsetY) MapTo(WorldBloomLevel source)
    {
        return ((double)Width * Stride / (source.Width * source.Stride),
            (double)Height * Stride / (source.Height * source.Stride),
            ((double)MinX - source.MinX) / (source.Width * source.Stride),
            ((double)MinY - source.MinY) / (source.Height * source.Stride));
    }
}
