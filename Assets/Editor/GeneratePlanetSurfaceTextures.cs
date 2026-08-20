#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Bakes high-resolution 2K equirectangular planet texture maps:
    /// 1. Albedo (Continents, volcanic basalt basins, sulfur crusts, mountain ridges)
    /// 2. Normal (Tangent/object micro-relief and crater slope normal map)
    /// 3. Emission (Hot geothermal magma fissure network)
    /// 4. Clouds (Atmospheric zonal bands, cyclone storm vortices, filaments with Alpha)
    /// </summary>
    internal static class GeneratePlanetSurfaceTextures
    {
        private const string AlbedoPath = "Assets/Textures/UI/planet_albedo_equirect.png";
        private const string NormalPath = "Assets/Textures/UI/planet_normal_equirect.png";
        private const string EmissionPath = "Assets/Textures/UI/planet_emission_equirect.png";
        private const string CloudsPath = "Assets/Textures/UI/planet_clouds_equirect.png";

        private const int Width = 4096;
        private const int Height = 2048;

        [MenuItem("Fodinae/Art/Generate Planet Surface Textures (4K)")]
        public static void Generate()
        {
            var albedoPixels = new Color32[Width * Height];
            var normalPixels = new Color32[Width * Height];
            var emissionPixels = new Color32[Width * Height];
            var cloudPixels = new Color32[Width * Height];

            // Color Palette
            var basaltColor = new Color(0.035f, 0.038f, 0.052f);
            var regolithColor = new Color(0.16f, 0.09f, 0.05f);
            var crustColor = new Color(0.26f, 0.22f, 0.16f);
            var peakColor = new Color(0.32f, 0.35f, 0.40f);
            var magmaColor = new Color(1.0f, 0.35f, 0.08f);
            var cloudColor = new Color(0.92f, 0.94f, 0.98f);

            const float basinLevel = 0.38f;
            const float peakLevel = 0.68f;
            const float crackThreshold = 0.82f;
            const float bumpStrength = 2.2f;

            Parallel.For(0, Height, y =>
            {
                float phi = (y + 0.5f) / Height * Mathf.PI;
                for (int x = 0; x < Width; x++)
                {
                    float theta = (x + 0.5f) / Width * Mathf.PI * 2f;
                    Vector3 dir = DirFromAngles(theta, phi);

                    // 1. Elevation & Terrain
                    float elev = Elevation(dir);

                    // Normal Calculation via finite differences
                    Vector3 up = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.right;
                    Vector3 tangent = Vector3.Normalize(Vector3.Cross(up, dir));
                    Vector3 bitangent = Vector3.Cross(dir, tangent);
                    const float eps = 0.008f;
                    float elevT = Elevation(Vector3.Normalize(dir + (tangent * eps)));
                    float elevB = Elevation(Vector3.Normalize(dir + (bitangent * eps)));
                    Vector2 slopeVec = new Vector2(elevT - elev, elevB - elev) / eps;
                    float slope = Mathf.Clamp01(slopeVec.magnitude * 0.09f);

                    float poleDamp = Mathf.Clamp01((0.98f - Mathf.Abs(dir.y)) / 0.08f);
                    Vector3 normOS = Vector3.Normalize(dir - (((tangent * slopeVec.x) + (bitangent * slopeVec.y)) * bumpStrength * 0.05f * poleDamp));

                    // 2. Albedo
                    Color albedo = Color.Lerp(crustColor, regolithColor, SmoothStep(0.10f, 0.42f, slope));
                    albedo = Color.Lerp(albedo, basaltColor, SmoothStep(0.38f, 0.80f, slope));
                    float basin = 1.0f - SmoothStep(basinLevel - 0.10f, basinLevel + 0.14f, elev);
                    albedo = Color.Lerp(albedo, crustColor, basin * (1.0f - SmoothStep(0.30f, 0.65f, slope)) * 0.75f);
                    albedo = Color.Lerp(albedo, peakColor, SmoothStep(peakLevel, peakLevel + 0.22f, elev));

                    // 3. Magma Emission
                    float faultField = RidgedField(dir * 14.0f, 4);
                    float crack = SmoothStep(crackThreshold, 1.0f, faultField);
                    float breakUp = SmoothStep(-0.25f, 0.35f, SmoothField(dir * (14.0f * 2.7f), 3));
                    float lowGround = 1.0f - SmoothStep(basinLevel, basinLevel + 0.30f, elev);
                    float crackMask = crack * breakUp * lowGround;
                    Color emissive = magmaColor * crackMask;

                    // 4. Clouds (Zonal bands + storm cyclones)
                    Vector3 cp = dir * 5.2f;
                    Vector3 warp = new Vector3(
                        ValueNoise(cp + new Vector3(11.3f, 5.1f, 27.7f)),
                        ValueNoise(cp + new Vector3(47.9f, 63.2f, 8.4f)),
                        ValueNoise(cp + new Vector3(83.1f, 19.6f, 51.3f)));
                    float cloudCov = SmoothField(cp + (warp * 1.2f), 4);
                    float wobble = ValueNoise(cp * 0.7f) * 0.35f;
                    float bands = Mathf.Sin(((dir.y + wobble) * 5.0f * Mathf.PI) + 1.1f);
                    cloudCov += bands * 0.28f;
                    float cloudAlpha = SmoothStep(0.48f, 0.48f + 0.32f, (cloudCov * 0.5f) + 0.5f);

                    int idx = (y * Width) + x;
                    albedoPixels[idx] = ToColor32(albedo, 1.0f);
                    normalPixels[idx] = new Color32(
                        (byte)(Mathf.Clamp01((normOS.x * 0.5f) + 0.5f) * 255),
                        (byte)(Mathf.Clamp01((normOS.y * 0.5f) + 0.5f) * 255),
                        (byte)(Mathf.Clamp01((normOS.z * 0.5f) + 0.5f) * 255),
                        255);
                    emissionPixels[idx] = ToColor32(emissive, crackMask);
                    cloudPixels[idx] = ToColor32(cloudColor, cloudAlpha);
                }
            });

            SaveTexture(AlbedoPath, albedoPixels, false);
            SaveTexture(NormalPath, normalPixels, false);
            SaveTexture(EmissionPath, emissionPixels, true);
            SaveTexture(CloudsPath, cloudPixels, true);

            AssetDatabase.Refresh();
            Debug.Log("[GeneratePlanetSurfaceTextures] Successfully baked all 2K planet texture maps!");
        }

        private static void SaveTexture(string path, Color32[] pixels, bool alphaIsTransparency)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            byte[] png = tex.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, png);
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        private static Vector3 DirFromAngles(float theta, float phi)
        {
            float sinPhi = Mathf.Sin(phi);
            return new Vector3(sinPhi * Mathf.Cos(theta), Mathf.Cos(phi), sinPhi * Mathf.Sin(theta));
        }

        private static float Elevation(Vector3 dir)
        {
            Vector3 c = dir * 3.2f;
            Vector3 warp = new Vector3(
                ValueNoise(c + new Vector3(17.1f, 3.2f, 8.9f)),
                ValueNoise(c + new Vector3(43.7f, 21.4f, 2.6f)),
                ValueNoise(c + new Vector3(91.3f, 12.8f, 33.1f)));

            float continents = SmoothField(c + (warp * 0.65f), 4);
            float elev = Mathf.Clamp01((continents * 0.5f) + 0.5f);

            float uplift = SmoothStep(0.40f, 0.78f, elev);
            float ranges = RidgedField(dir * 14.0f, 4);
            elev += ranges * uplift * 0.45f;

            elev += SmoothField(dir * 180.0f, 3) * 0.35f * 0.12f;
            return Mathf.Clamp01(elev);
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

        private static Color32 ToColor32(Color c, float alpha)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(c.r) * 255),
                (byte)(Mathf.Clamp01(c.g) * 255),
                (byte)(Mathf.Clamp01(c.b) * 255),
                (byte)(Mathf.Clamp01(alpha) * 255));
        }
    }
}
