#nullable enable

using System;
using UnityEngine;

namespace Kern.Tests.World;

public readonly record struct TerrainQuantizedPolygon(
    Vector2 Corner00,
    Vector2 Corner10,
    Vector2 Corner11,
    Vector2 Corner01)
{
    public Vector2 GetCorner(int index)
    {
        return index switch
        {
            0 => Corner00,
            1 => Corner10,
            2 => Corner11,
            3 => Corner01,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }
}

public static class TerrainQuantizationOracle
{
    public const int GridSize = 32;
    private const float Epsilon = 0.000001f;

    public static bool[,] Rasterize(TerrainQuantizedPolygon polygon)
    {
        var result = new bool[GridSize, GridSize];
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                Vector2 sample = new(
                    (x + 0.5f) / GridSize,
                    (y + 0.5f) / GridSize);
                result[x, y] = Contains(polygon, sample);
            }
        }

        return result;
    }

    public static bool Contains(TerrainQuantizedPolygon polygon, Vector2 sample)
    {
        float signedArea = 0f;
        for (int index = 0; index < 4; index++)
        {
            signedArea += Cross(polygon.GetCorner(index), polygon.GetCorner((index + 1) & 3));
        }

        if (Mathf.Abs(signedArea) <= Epsilon)
        {
            return false;
        }

        for (int index = 0; index < 4; index++)
        {
            Vector2 start = polygon.GetCorner(index);
            Vector2 end = polygon.GetCorner((index + 1) & 3);
            float cross = Cross(end - start, sample - start);
            if (Mathf.Abs(cross) <= Epsilon &&
                sample.x >= Mathf.Min(start.x, end.x) - Epsilon &&
                sample.x <= Mathf.Max(start.x, end.x) + Epsilon &&
                sample.y >= Mathf.Min(start.y, end.y) - Epsilon &&
                sample.y <= Mathf.Max(start.y, end.y) + Epsilon)
            {
                return true;
            }

        }

        bool inside = false;
        for (int index = 0; index < 4; index++)
        {
            Vector2 start = polygon.GetCorner(index);
            Vector2 end = polygon.GetCorner((index + 1) & 3);
            float cross = Cross(end - start, sample - start);
            bool crossesScanline = (start.y > sample.y) != (end.y > sample.y);
            if (crossesScanline)
            {
                float xAtScanline = start.x +
                    ((sample.y - start.y) * (end.x - start.x) / (end.y - start.y));
                if (sample.x < xAtScanline)
                {
                    inside = !inside;
                }
            }
        }

        // The crossing rule handles both winding directions and concave
        // quadrilaterals without reusing production shader code.
        return inside;
    }

    public static TerrainQuantizedPolygon FromSteps(
        int x00,
        int y00,
        int x10,
        int y10,
        int x11,
        int y11,
        int x01,
        int y01)
    {
        return new TerrainQuantizedPolygon(
            new Vector2(x00 / (float)GridSize, y00 / (float)GridSize),
            new Vector2(1f + x10 / (float)GridSize, y10 / (float)GridSize),
            new Vector2(1f + x11 / (float)GridSize, 1f + y11 / (float)GridSize),
            new Vector2(x01 / (float)GridSize, 1f + y01 / (float)GridSize));
    }

    public static int CountOccupied(bool[,] bitmap)
    {
        int count = 0;
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                count += bitmap[x, y] ? 1 : 0;
            }
        }

        return count;
    }

    public static string Diff(bool[,] expected, bool[,] actual)
    {
        var differences = new System.Text.StringBuilder();
        for (int y = GridSize - 1; y >= 0; y--)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (expected[x, y] == actual[x, y])
                {
                    continue;
                }

                differences.Append($"({x},{y}) expected={expected[x, y]} actual={actual[x, y]} ");
            }
        }

        return differences.ToString();
    }

    private static float Cross(Vector2 left, Vector2 right) =>
        (left.x * right.y) - (left.y * right.x);
}
