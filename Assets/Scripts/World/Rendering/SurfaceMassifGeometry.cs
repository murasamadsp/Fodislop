#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World;

/// <summary>Closed, individually shaped basalt masses and stepped outcrops, merged per visible world segment.</summary>
internal static class SurfaceMassifGeometry
{
    private const int Sides = 9;
    private const int Rings = 16;

    public static Mesh Create(int segmentIndex, int seed, float widthCells, int layer)
    {
        long featureCoordinate = 0;
        int sampleChannel = 0;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var indices = new List<int>();
        float worldOrigin = segmentIndex * widthCells;
        bool foreground = layer == 2;
        using var baseMesh = new OwnedMesh(SurfaceRockGeometry.Create(segmentIndex, seed, widthCells, layer != 2));
        Vector3[] baseVertices = baseMesh.Value.vertices;
        int[] baseIndices = baseMesh.Value.triangles;
        for (int triangle = 0; triangle < baseIndices.Length; triangle += 3)
        {
            Vector3 a = baseVertices[baseIndices[triangle]];
            Vector3 b = baseVertices[baseIndices[triangle + 1]];
            Vector3 c = baseVertices[baseIndices[triangle + 2]];
            if (!foreground)
            {
                a.y *= 0.22f;
                b.y *= 0.22f;
                c.y *= 0.22f;
            }

            AddTriangle(a, b, c);
        }

        long segmentCoordinate = (long)System.Math.Round(worldOrigin * 1024.0);
        float density = SurfaceSpatialHash.Sample(segmentCoordinate, seed, layer + 30000);
        int count = foreground ? 20 + (int)(density * 15f) : layer == 0 ? 5 + (int)(density * 7f) : 3 + (int)(density * 3f);
        for (int rock = 0; rock < count; rock++)
        {
            float x = (rock + 0.5f) / count * widthCells;
            featureCoordinate = (long)System.Math.Round((worldOrigin + x) * 1024.0);
            sampleChannel = layer * 10000;
            x += Range(-0.35f, 0.35f) * widthCells / count;
            float radius = foreground ? Range(1.2f, 3.0f) : layer == 0 ? Range(8f, 19f) : Range(4.5f, 10.5f);
            float height = foreground ? Range(0.8f, 3.1f) : layer == 0 ? Range(8f, 19f) : Range(4f, 11f);
            float depth = foreground ? Range(0.1f, 1.8f) : Range(0.1f, 6f);
            AddMass(new Vector3(x, foreground ? -0.25f : 0f, depth), radius, height);
            if (layer == 1)
            {
                AddExposedColumns(new Vector3(x, 0f, depth), radius, height);
            }
            if (!foreground)
            {
                AddMass(new Vector3(x - radius * 0.65f, 0f, depth - 0.1f), radius * 0.9f, height * 0.55f);
                AddMass(new Vector3(x + radius * 0.7f, 0f, depth - 0.2f), radius * 0.65f, height * 0.38f);
                if (Range(0f, 1f) > 0.4f)
                {
                    AddMass(new Vector3(x + radius * Range(-0.55f, 0.55f), height * 0.32f, depth - radius * 0.18f),
                        radius * Range(0.2f, 0.4f), height * Range(0.42f, 0.65f));
                }
            }
        }

        var mesh = new Mesh
        {
            name = $"Surface closed basalt masses {layer}/{segmentIndex}",
            indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;

        float Range(float minimum, float maximum) =>
            minimum + SurfaceSpatialHash.Sample(featureCoordinate, seed, sampleChannel++) * (maximum - minimum);

        void AddMass(Vector3 center, float radius, float height)
        {
            if (foreground)
            {
                AddBasaltStack(center, radius, height);
                return;
            }

            var points = new Vector3[(Rings + 1) * Sides];
            float taperPower = Range(0.22f, 0.55f);
            float crownStart = Range(0.55f, 0.82f);
            float ledgeWidth = Range(0.65f, 0.95f);
            var columnRadii = new float[Sides];
            var columnHeights = new float[Sides];
            for (int side = 0; side < Sides; side++)
            {
                columnRadii[side] = Range(0.68f, 1.2f);
                columnHeights[side] = Range(0.72f, 1.12f);
            }
            float tilt = Range(-0.4f, 0.4f) * radius;
            float rotation = Range(0f, 6.28318f);
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = (float)ring / Rings;
                float taper = t < crownStart
                    ? Mathf.Lerp(1f, ledgeWidth, t / crownStart)
                    : ledgeWidth * Mathf.Pow((1f - t) / (1f - crownStart), taperPower);
                // A broken crown has a finite footprint. Collapsing every column to
                // one tip produced the smooth pyramids visible in the rejected frame.
                taper = Mathf.Max(taper, 0.24f);
                float terrace = 1f + Mathf.Floor((1f - t) * 7f) * 0.045f;
                for (int side = 0; side < Sides; side++)
                {
                    float angle = side * (Mathf.PI * 2f / Sides) + rotation;
                    float rugged = columnRadii[side] * terrace * Mathf.Lerp(1f, 0.92f, t);
                    float y = ring == 0 ? 0f : height * t * Mathf.Lerp(1f, columnHeights[side], t * t);
                    points[ring * Sides + side] = center + new Vector3(
                        Mathf.Cos(angle) * radius * taper * rugged + tilt * t,
                        y,
                        Mathf.Sin(angle) * radius * taper * rugged * 0.72f);
                }
            }

            for (int ring = 0; ring < Rings; ring++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    int next = (side + 1) % Sides;
                    Vector3 a = points[ring * Sides + side];
                    Vector3 b = points[ring * Sides + next];
                    Vector3 c = points[(ring + 1) * Sides + side];
                    Vector3 d = points[(ring + 1) * Sides + next];
                    AddTriangle(a, c, b);
                    AddTriangle(b, c, d);
                }
            }

            Vector3 tip = center + new Vector3(tilt, height * 0.83f, 0f);
            for (int side = 0; side < Sides; side++)
            {
                AddTriangle(points[Rings * Sides + side], tip, points[Rings * Sides + (side + 1) % Sides]);
                AddTriangle(points[(side + 1) % Sides], center, points[side]);
            }
        }

        void AddExposedColumns(Vector3 center, float radius, float height)
        {
            const int Columns = 7;
            float blockWidth = radius * 2f / Columns;
            for (int column = 0; column < Columns; column++)
            {
                float across = (column + 0.5f) / Columns * 2f - 1f;
                float columnHeight = height * Mathf.Pow(1f - Mathf.Abs(across), 0.45f) * Range(0.62f, 0.94f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(columnHeight / blockWidth));
                for (int step = 0; step < steps; step++)
                {
                    float y = step * blockWidth;
                    float blockHeight = Mathf.Min(blockWidth, columnHeight - y);
                    float faceDepth = -radius * 0.72f * Mathf.Sqrt(1f - across * across);
                    AddBasaltStack(center + new Vector3(across * radius + Range(-0.12f, 0.12f) * blockWidth,
                        y, faceDepth - blockWidth * 0.18f), blockWidth * Range(0.43f, 0.53f), blockHeight);
                }
            }
        }

        void AddBasaltStack(Vector3 center, float radius, float height)
        {
            int levels = 2 + (int)Range(0f, 2f);
            float angle = Range(-0.3f, 0.3f);
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            for (int level = 0; level < levels; level++)
            {
                float halfWidth = radius * (1f - level * 0.09f) * Range(0.8f, 1.1f);
                float halfDepth = halfWidth * Range(0.55f, 0.95f);
                float y0 = height * level / levels;
                float y1 = height * (level + 1) / levels;
                float shift = Range(-0.12f, 0.12f) * radius;
                var corners = new Vector3[8];
                for (int corner = 0; corner < 8; corner++)
                {
                    float x = ((corner & 1) == 0 ? -halfWidth : halfWidth) + shift;
                    float z = (corner & 2) == 0 ? -halfDepth : halfDepth;
                    float y = (corner & 4) == 0 ? y0 : y1 + Range(-0.04f, 0.04f) * height
                        + (z + halfDepth) * 0.18f;
                    corners[corner] = center + new Vector3(x * cosine - z * sine, y, x * sine + z * cosine);
                }

                Face(0, 4, 1, 5);
                Face(2, 3, 6, 7);
                Face(0, 2, 4, 6);
                Face(1, 5, 3, 7);
                Face(4, 6, 5, 7);
                Face(0, 1, 2, 3);

                void Face(int a, int b, int c, int d)
                {
                    AddTriangle(corners[a], corners[b], corners[c]);
                    AddTriangle(corners[c], corners[b], corners[d]);
                }
            }
        }

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uv.Add(new Vector2(worldOrigin + a.x, a.y));
            uv.Add(new Vector2(worldOrigin + b.x, b.y));
            uv.Add(new Vector2(worldOrigin + c.x, c.y));
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
        }
    }

    private sealed class OwnedMesh : IDisposable
    {
        private readonly Mesh _value;

        public OwnedMesh(Mesh value)
        {
            _value = value;
        }

        public Mesh Value => _value;
        public void Dispose() => SurfaceMeshUtilities.DestroyOwned(_value);
    }
}
