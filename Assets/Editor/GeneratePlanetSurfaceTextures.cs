#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Bakes the GJ-1132b-style rocky planet surface (dark basalt valleys, ridged mountains,
    /// pale mineral peaks, sparse red fissure glow) to an equirectangular albedo texture.
    /// Baking offline (rather than computing it live in a shader) lets the result be inspected
    /// as a plain image file before it is ever wired into the scene.
    /// </summary>
    internal static class GeneratePlanetSurfaceTextures
    {
        private const string OutputPath = "Assets/Textures/UI/planet_albedo_equirect.png";
        private const int Width = 1536;
        private const int Height = 768;

        [MenuItem("Fodinae/Art/Generate Planet Surface Texture")]
        public static void Generate()
        {
            var darkColor = new Color(0.015f, 0.025f, 0.03f);
            var baseColor = new Color(0.09f, 0.16f, 0.07f);
            var lightColor = new Color(0.5f, 0.46f, 0.34f);
            var magmaColor = new Color(0.95f, 0.18f, 0.07f);

            // Direction TOWARD the light, in the same object-space frame the live camera views
            // the sphere from (camera sits on -Z looking at +Z, so the visible hemisphere's
            // normals point mostly toward -Z — this must have a negative Z to light that side).
            Vector3 lightDir = new Vector3(-0.4f, 0.45f, -0.8f).normalized;
            const float minBrightness = 0.22f;
            const float riftLevel = 0.16f;
            const float rockLevel = 0.36f;
            const float peakLevel = 0.6f;
            const float bumpStrength = 1.6f;
            const float noiseScale = 4.5f;
            const float ridgeScale = 10.0f;

            var rng = new System.Random(1132);
            Vector3[] fissurePoints = new Vector3[9];
            float[] fissureRadius = new float[9];
            for (int i = 0; i < fissurePoints.Length; i++)
            {
                Vector3 p;
                float elev;
                int attempts = 0;
                do
                {
                    p = RandomUnitVector(rng);
                    elev = Elevation(p, noiseScale, ridgeScale);
                    attempts++;
                }
                while (elev > riftLevel * 0.7f && attempts < 80);

                fissurePoints[i] = p;
                fissureRadius[i] = 0.028f + ((float)rng.NextDouble() * 0.02f);
            }

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                float phi = (y + 0.5f) / Height * Mathf.PI;
                for (int x = 0; x < Width; x++)
                {
                    float theta = (x + 0.5f) / Width * Mathf.PI * 2f;
                    Vector3 dir = DirFromAngles(theta, phi);

                    float elevC = Elevation(dir, noiseScale, ridgeScale);

                    Vector3 up = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
                    Vector3 tangent = Vector3.Normalize(Vector3.Cross(up, dir));
                    Vector3 bitangent = Vector3.Cross(dir, tangent);
                    const float eps = 0.015f;
                    float elevT = Elevation(Vector3.Normalize(dir + (tangent * eps)), noiseScale, ridgeScale);
                    float elevB = Elevation(Vector3.Normalize(dir + (bitangent * eps)), noiseScale, ridgeScale);
                    Vector3 grad = (tangent * ((elevT - elevC) / eps)) + (bitangent * ((elevB - elevC) / eps));

                    // Damp the bump near the poles: the tangent/bitangent frame and the finite
                    // difference both become numerically unstable as dir.y -> +-1, which was
                    // producing a spurious bright band right at the pole rows.
                    float poleDamp = Mathf.Clamp01((0.98f - Mathf.Abs(dir.y)) / 0.08f);
                    Vector3 normal = Vector3.Normalize(dir - (grad * bumpStrength * poleDamp));

                    float rockT = SmoothStep(riftLevel, rockLevel, elevC);
                    float peakT = SmoothStep(rockLevel, peakLevel, elevC);
                    Color albedo = Color.Lerp(darkColor, baseColor, rockT);
                    albedo = Color.Lerp(albedo, lightColor, peakT);

                    float ndotl = Vector3.Dot(normal, lightDir);
                    float lit = Mathf.Lerp(minBrightness, 1f, Mathf.Clamp01((ndotl * 0.5f) + 0.5f));
                    Color color = albedo * lit;

                    float glow = 0f;
                    for (int i = 0; i < fissurePoints.Length; i++)
                    {
                        float cosDist = Vector3.Dot(dir, fissurePoints[i]);

                        // Soft edge must scale with the (tiny) fissure radius itself -
                        // a fixed absolute softening constant here previously dwarfed
                        // the intended radius and blew every dot up into a big blob.
                        float softEdge = fissureRadius[i] * 0.5f;
                        float cosHard = Mathf.Cos(fissureRadius[i]);
                        float cosSoft = Mathf.Cos(fissureRadius[i] + softEdge);
                        float g = Mathf.Clamp01(Mathf.InverseLerp(cosSoft, cosHard, cosDist));
                        glow = Mathf.Max(glow, g);
                    }

                    color += magmaColor * glow * 1.5f;

                    pixels[(y * Width) + x] = ToColor32(color);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            byte[] png = tex.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllBytes(OutputPath, png);
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(OutputPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;

            // Equirectangular sphere UVs stretch severely at the silhouette (view ray tangent
            // to the sphere) — without mipmaps there is nothing for the GPU to filter with
            // there, and it aliases into a bright fringe/ring right at the sphere's edge.
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Debug.Log($"[GeneratePlanetSurfaceTextures] Wrote {OutputPath} ({Width}x{Height}).");
        }

        private static Vector3 DirFromAngles(float theta, float phi)
        {
            float sinPhi = Mathf.Sin(phi);
            return new Vector3(sinPhi * Mathf.Cos(theta), Mathf.Cos(phi), sinPhi * Mathf.Sin(theta));
        }

        private static Vector3 RandomUnitVector(System.Random rng)
        {
            float z = (float)((rng.NextDouble() * 2.0) - 1.0);
            float t = (float)(rng.NextDouble() * Math.PI * 2.0);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - (z * z)));
            return new Vector3(r * Mathf.Cos(t), z, r * Mathf.Sin(t));
        }

        private static float Elevation(Vector3 dir, float noiseScale, float ridgeScale)
        {
            // The smooth field defines broad continent/mountain-belt shape; ridged detail is
            // masked by it so jagged terrain clusters into distinct mountain regions against flat
            // plains, instead of cracking uniformly across the whole sphere.
            float smooth = SmoothField(dir * noiseScale, 4);
            float mask = SmoothStep(0.25f, 0.65f, smooth);
            float ridged = RidgedField(dir * ridgeScale, 3) * (0.15f + (mask * 0.85f));
            return Mathf.Clamp01((smooth * 0.45f) + (ridged * 0.65f));
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-5f));
            return t * t * (3f - (2f * t));
        }

        private static float Hash(Vector3 p)
        {
            float d = Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 45.164f));
            float s = Mathf.Sin(d) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        private static float ValueNoise(Vector3 p)
        {
            Vector3 i = new(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            Vector3 f = p - i;
            Vector3 u = new(f.x * f.x * (3f - (2f * f.x)), f.y * f.y * (3f - (2f * f.y)), f.z * f.z * (3f - (2f * f.z)));

            float n000 = Hash(i + new Vector3(0, 0, 0));
            float n100 = Hash(i + new Vector3(1, 0, 0));
            float n010 = Hash(i + new Vector3(0, 1, 0));
            float n110 = Hash(i + new Vector3(1, 1, 0));
            float n001 = Hash(i + new Vector3(0, 0, 1));
            float n101 = Hash(i + new Vector3(1, 0, 1));
            float n011 = Hash(i + new Vector3(0, 1, 1));
            float n111 = Hash(i + new Vector3(1, 1, 1));

            float nx00 = Mathf.Lerp(n000, n100, u.x);
            float nx10 = Mathf.Lerp(n010, n110, u.x);
            float nx01 = Mathf.Lerp(n001, n101, u.x);
            float nx11 = Mathf.Lerp(n011, n111, u.x);
            float nxy0 = Mathf.Lerp(nx00, nx10, u.y);
            float nxy1 = Mathf.Lerp(nx01, nx11, u.y);
            return Mathf.Lerp(nxy0, nxy1, u.z);
        }

        private static Vector3 RotateOctave(Vector3 p)
        {
            float x = (p.x * 0.8f) + (p.y * 0.6f);
            float y = (-p.x * 0.6f) + (p.y * 0.8f);
            return new Vector3(x, y, p.z);
        }

        private static float SmoothField(Vector3 p, int octaves)
        {
            float amp = 0.5f;
            float sum = 0f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(p);
                norm += amp;
                p = (RotateOctave(p) * 2.03f) + new Vector3(11.3f, 5.7f, 8.1f);
                amp *= 0.5f;
            }

            return sum / Mathf.Max(norm, 1e-4f);
        }

        private static float RidgedField(Vector3 p, int octaves)
        {
            float amp = 0.5f;
            float sum = 0f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = ValueNoise(p);
                float r = 1f - Mathf.Abs((2f * n) - 1f);
                r *= r;
                sum += amp * r;
                norm += amp;
                p = (RotateOctave(p) * 2.03f) + new Vector3(3.7f, 9.1f, 2.3f);
                amp *= 0.5f;
            }

            return sum / Mathf.Max(norm, 1e-4f);
        }

        private static Color32 ToColor32(Color c)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(c.r) * 255),
                (byte)(Mathf.Clamp01(c.g) * 255),
                (byte)(Mathf.Clamp01(c.b) * 255),
                255);
        }
    }
}
