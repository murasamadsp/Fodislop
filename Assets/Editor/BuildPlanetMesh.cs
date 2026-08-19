#nullable enable

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Generates the sphere meshes used by the menu planet.
    //
    // Unity's PrimitiveType.Sphere is a 20x20 UV sphere - about 80 segments
    // around the equator. At the size the menu planet is actually drawn (~800px
    // across) that puts a facet edge every ~30px, and the straight chords are
    // plainly visible along the limb. No amount of shader work fixes a silhouette
    // that is genuinely a polygon.
    //
    // An icosphere is used rather than a denser UV sphere because a UV sphere
    // crowds its vertices at the poles and wastes them there while still being
    // coarse at the equator, which is exactly where this planet's silhouette is.
    internal static class BuildPlanetMesh
    {
        public const string PlanetMeshPath = MeshFolder + "/PlanetIcosphere.asset";
        public const string ShellMeshPath = MeshFolder + "/PlanetShellIcosphere.asset";

        private const string MeshFolder = "Assets/Meshes";

        // 6 subdivisions => 81,920 triangles, ~286 segments around the silhouette,
        // so a facet spans well under 10px at menu size. 5 is enough for the
        // atmosphere shell: its shader intersects the sphere analytically, so the
        // mesh only has to cover the right screen area, not define the curve.
        private const int PlanetSubdivisions = 6;
        private const int ShellSubdivisions = 5;

        [MenuItem("Fodinae/Art/Build Planet Meshes")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Meshes");
            }

            WriteMesh(PlanetMeshPath, "PlanetIcosphere", PlanetSubdivisions);
            WriteMesh(ShellMeshPath, "PlanetShellIcosphere", ShellSubdivisions);
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildPlanetMesh] Planet meshes generated.");
        }

        private static void WriteMesh(string path, string name, int subdivisions)
        {
            Mesh generated = CreateIcosphere(subdivisions);
            generated.name = name;

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return;
            }

            // Overwrite in place so any scene reference to this mesh survives.
            existing.Clear();
            existing.indexFormat = generated.indexFormat;
            existing.vertices = generated.vertices;
            existing.normals = generated.normals;
            existing.triangles = generated.triangles;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
        }

        private static Mesh CreateIcosphere(int subdivisions)
        {
            // Radius 0.5 so the mesh is a drop-in replacement for Unity's own
            // sphere primitive - every transform scale in the rig assumes that.
            const float t = 1.618033988749895f;

            var vertices = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };

            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            var midpointCache = new Dictionary<long, int>();

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(faces.Count * 4);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i];
                    int b = faces[i + 1];
                    int c = faces[i + 2];

                    int ab = Midpoint(a, b, vertices, midpointCache);
                    int bc = Midpoint(b, c, vertices, midpointCache);
                    int ca = Midpoint(c, a, vertices, midpointCache);

                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                faces = next;
            }

            var positions = new Vector3[vertices.Count];
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 n = vertices[i].normalized;
                normals[i] = n;
                positions[i] = n * 0.5f;
            }

            var mesh = new Mesh
            {
                // 6 subdivisions is 40,962 vertices - under the 16-bit limit, but
                // set explicitly so raising the subdivision count later does not
                // silently corrupt the mesh.
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTriangles(faces, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            Vector3 mid = (vertices[a] + vertices[b]) * 0.5f;
            vertices.Add(mid);
            int index = vertices.Count - 1;
            cache[key] = index;
            return index;
        }
    }
}
