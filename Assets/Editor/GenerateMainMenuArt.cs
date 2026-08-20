#nullable enable

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Генератор процедурного арт-фона для Главного меню (Планета Fodinae, атмосфера, магма).
    /// В точности воспроизводит кинематографическую планету и корону из visual/main-menu.html.
    /// </summary>
    internal static class GenerateMainMenuArt
    {
        private const string OutputFolder = "Assets/Textures/UI";

        [MenuItem("Fodinae/Art/Generate Main Menu Gradient Art")]
        public static void Generate()
        {
            Directory.CreateDirectory(OutputFolder);

            SaveTexture(GenerateSpaceBackground(1920, 1080), $"{OutputFolder}/mm_space_bg.png");
            SaveTexture(GenerateShade(1024, 1024), $"{OutputFolder}/mm_shade.png");
            SaveTexture(GenerateHighResPlanet(1024), $"{OutputFolder}/mm_planet.png");

            AssetDatabase.Refresh();
            Debug.Log("[GenerateMainMenuArt] Wrote high-resolution mm_space_bg.png, mm_shade.png, and mm_planet.png.");
        }

        private static Texture2D GenerateSpaceBackground(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[width * height];

            // No nebulae.
            //
            // This used to paint two large radial blobs - a saturated cyan one
            // centred at (0.75w, 0.55h) and an orange one at (0.85w, 0.35h) -
            // which is exactly where the planet sits. Two problems: a plain
            // radial gradient reads as a soft-brush smudge rather than as any
            // real astronomical object, and cyan actively fights the setting,
            // which is a sulfur-yellow world lit by a red dwarf. Removing them
            // also stops the background from competing with the planet for
            // attention in the one spot the eye already goes.
            //
            // What is left is a near-black sky with a very slight warm lift
            // toward the horizon-ward edge, which is what deep space actually
            // looks like behind a lit body.
            var spaceDeep = new Color(0.011f, 0.017f, 0.030f);
            var spaceDark = new Color(0.003f, 0.005f, 0.011f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = Color.Lerp(spaceDeep, spaceDark, (float)y / height);

                    // Dither: a smooth low-frequency ramp bands visibly under
                    // 8-bit quantization (faint horizontal seams). A tiny
                    // per-pixel noise offset breaks the banding up.
                    float dither = (HashNoise(x, y) - 0.5f) * (1.5f / 255f);
                    c.r += dither;
                    c.g += dither;
                    c.b += dither;

                    pixels[(y * width) + x] = ToColor32(c, 1f);
                }
            }

            // No baked star points.
            //
            // The sky is now procedural: MenuStarfield blits a twinkling field
            // into a RenderTexture that the UI draws as the background Image, so
            // stars baked into this fallback PNG would sit under the procedural
            // field as a second, static constellation whenever both are present.
            // The texture keeps only the gradient.
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D GenerateShade(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 0.75f, t));
                alpha = Mathf.Pow(alpha, 0.85f) * 0.96f;
                var color = new Color32(3, 6, 12, (byte)(alpha * 255));
                for (int y = 0; y < height; y++)
                {
                    pixels[(y * width) + x] = color;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D GenerateHighResPlanet(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[size * size];
            Vector2 center = new(size * 0.5f, size * 0.5f);
            float radius = size * 0.40f;
            float atmosphereRadius = size * 0.48f;

            // Цвета терракотовой планеты Fodinae
            var surfaceBase = new Color(0.68f, 0.38f, 0.22f);    // Терракота
            var surfaceDark = new Color(0.18f, 0.10f, 0.08f);    // Темная порода
            var surfaceLight = new Color(0.85f, 0.52f, 0.32f);   // Светлый континент
            var magmaBright = new Color(1.0f, 0.55f, 0.22f);    // Лава #ff7a36
            var atmosphere = new Color(0.35f, 0.88f, 0.84f);     // Неоновый циан #56ddd4
            var nightShadow = new Color(0.015f, 0.025f, 0.04f);  // Теневая сторона

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new(x + 0.5f, y + 0.5f);
                    float dist = Vector2.Distance(p, center);

                    // Внешняя корона / свечение
                    if (dist > atmosphereRadius)
                    {
                        float outerGlow = Mathf.Clamp01(1f - ((dist - atmosphereRadius) / (size * 0.06f)));
                        outerGlow = Mathf.Pow(outerGlow, 2f) * 0.22f;
                        pixels[(y * size) + x] = ToColor32(atmosphere, outerGlow);
                        continue;
                    }

                    // Атмосферный обод
                    if (dist > radius)
                    {
                        float glowT = 1f - Mathf.InverseLerp(radius, atmosphereRadius, dist);
                        float alpha = Mathf.Pow(glowT, 1.4f) * 0.95f;
                        pixels[(y * size) + x] = ToColor32(atmosphere, alpha);
                        continue;
                    }

                    // Поверхность планеты
                    float surfaceT = dist / radius;
                    Color baseColor = Color.Lerp(surfaceLight, surfaceBase, surfaceT);

                    // Рельефные пятна
                    float blobA = 1f - Mathf.Clamp01(Vector2.Distance(p, center + new Vector2(-radius * 0.25f, -radius * 0.25f)) / (radius * 0.6f));
                    float blobB = 1f - Mathf.Clamp01(Vector2.Distance(p, center + new Vector2(radius * 0.35f, -radius * 0.15f)) / (radius * 0.45f));
                    float blobC = 1f - Mathf.Clamp01(Vector2.Distance(p, center + new Vector2(-radius * 0.1f, radius * 0.35f)) / (radius * 0.5f));
                    float mottle = Mathf.Clamp01((blobA * 0.6f) + (blobB * 0.4f) + (blobC * 0.5f));
                    Color surface = Color.Lerp(baseColor, surfaceDark, (1f - mottle) * 0.5f);

                    // Магматические разломы на границе тени
                    float magmaDist = Vector2.Distance(p, center + new Vector2(radius * 0.35f, radius * 0.2f));
                    if (magmaDist < radius * 0.3f && Mathf.Sin(x * 0.25f) * Mathf.Cos(y * 0.25f) > 0.4f)
                    {
                        surface = Color.Lerp(surface, magmaBright, 0.85f);
                    }

                    // Теневая ночь (затемнение нижней правой части)
                    float nightT = Mathf.Clamp01(Vector2.Distance(p, center + new Vector2(-radius * 0.6f, radius * 0.6f)) / (radius * 1.35f));
                    Color final = Color.Lerp(nightShadow, surface, nightT);

                    // Циановый обод по краю планеты (Rim light)
                    float rim = Mathf.Clamp01(Mathf.InverseLerp(radius * 0.84f, radius, dist));
                    final = Color.Lerp(final, atmosphere, rim * 0.85f);

                    pixels[(y * size) + x] = ToColor32(final, 1f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static float HashNoise(int x, int y)
        {
            float d = (x * 12.9898f) + (y * 78.233f);
            float s = Mathf.Sin(d) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        private static Color32 ToColor32(Color color, float alpha)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(color.r) * 255),
                (byte)(Mathf.Clamp01(color.g) * 255),
                (byte)(Mathf.Clamp01(color.b) * 255),
                (byte)(Mathf.Clamp01(alpha) * 255));
        }

        private static void SaveTexture(Texture2D texture, string path)
        {
            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(path, png);
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
