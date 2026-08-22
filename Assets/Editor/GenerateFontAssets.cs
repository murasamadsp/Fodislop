#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Fodinae.Editor
{
    [InitializeOnLoad]
    public static class GenerateFontAssets
    {
        static GenerateFontAssets()
        {
            EditorApplication.delayCall += EnsureAssets;
        }

        private static void EnsureAssets()
        {
            string[] fontFiles = { "Unbounded.ttf", "Exo2.ttf", "JetBrainsMono.ttf" };
            bool needsGeneration = false;

            foreach (string fontFile in fontFiles)
            {
                string assetName = Path.GetFileNameWithoutExtension(fontFile);
                string outPath = $"Assets/Resources/Fonts/{assetName}_SDF.asset";
                var asset = AssetDatabase.LoadAssetAtPath<FontAsset>(outPath);
                if (asset == null)
                {
                    needsGeneration = true;
                    break;
                }
            }

            if (needsGeneration)
            {
                GenerateFontAssetsOnly();
            }
        }

        [MenuItem("Fodinae/Generate UI Toolkit Font Assets")]
        public static void GenerateFontAssetsOnly()
        {
            string[] fontFiles = { "Unbounded.ttf", "Exo2.ttf", "JetBrainsMono.ttf" };

            foreach (string fontFile in fontFiles)
            {
                string fontPath = $"Assets/Resources/Fonts/{fontFile}";
                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font == null)
                {
                    continue;
                }

                string assetName = Path.GetFileNameWithoutExtension(fontFile);
                string outPath = $"Assets/Resources/Fonts/{assetName}_SDF.asset";

                try
                {
                    if (File.Exists(outPath))
                    {
                        AssetDatabase.DeleteAsset(outPath);
                    }

                    FontAsset fontAsset = FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (fontAsset != null)
                    {
                        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        AssetDatabase.CreateAsset(fontAsset, outPath);
                        if (fontAsset.material != null)
                        {
                            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                        }

                        if (fontAsset.atlasTextures != null)
                        {
                            foreach (Texture2D tex in fontAsset.atlasTextures)
                            {
                                if (tex != null)
                                {
                                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                                }
                            }
                        }

                        EditorUtility.SetDirty(fontAsset);
                        Debug.Log($"[GenerateFontAssets] Created true TextCore FontAsset: {outPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GenerateFontAssets] TextCore FontAsset creation for {fontFile}: {ex.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
#endif
