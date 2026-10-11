#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.World;

/// <summary>Presentation-only basalt heightfield. Coordinates and UV scale are in Unity Y-up cells.</summary>
internal static class SurfaceRockGeometry
{
    private const int Columns = 64;
    private const int Rows = 5;

    public static Mesh Create(int segmentIndex, int seed, float widthCells, bool middle)
    {
        var vertices = new List<Vector3>(Columns * Rows * 6);
        var uv = new List<Vector2>(Columns * Rows * 6);
        var triangles = new List<int>(Columns * Rows * 6);
        float originX = segmentIndex * widthCells;
        for (int x = 0; x < Columns; x++)
        {
            for (int row = 0; row < Rows; row++)
            {
                Vector3 a = Point(originX, x, row, widthCells, seed, middle);
                Vector3 b = Point(originX, x + 1, row, widthCells, seed, middle);
                Vector3 c = Point(originX, x, row + 1, widthCells, seed, middle);
                Vector3 d = Point(originX, x + 1, row + 1, widthCells, seed, middle);
                AddTriangle(a, c, b);
                AddTriangle(b, c, d);
            }
        }

        var mesh = new Mesh { name = $"Surface basalt {(middle ? "middle" : "near")} {segmentIndex}" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            uv.Add(new Vector2(a.x + originX, a.y));
            uv.Add(new Vector2(b.x + originX, b.y));
            uv.Add(new Vector2(c.x + originX, c.y));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }
    }

    private static Vector3 Point(float originX, int column, int row, float width, int seed, bool middle)
    {
        float localX = column * width / Columns;
        float worldX = originX + localX;
        float coarse = SurfaceSpatialHash.Noise(worldX, middle ? 18f : 2.4f, seed, 0);
        float fine = SurfaceSpatialHash.Noise(worldX, 0.6f, seed, row + 1);
        float height = middle ? 8f + coarse * 17f + fine * 2f : 0.12f + coarse * 0.4f + fine * 0.12f;
        float crossSection = row switch { 0 => 0f, 1 => 0.52f, 2 => 0.8f, 3 => 1f, 4 => 0.65f, _ => 0f, };
        return new Vector3(localX, height * crossSection, row * (middle ? 1.7f : 0.45f));
    }
}
