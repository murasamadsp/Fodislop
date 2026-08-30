#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Fodinae.Editor
{
    /// <summary>
    /// Bootstrap for CJK rendering. Creates Dynamic SDF font assets from the
    /// bundled Noto Sans SC / TC fonts (covering both the simplified and the
    /// traditional Chinese dictionaries) and wires them as the local fallback
    /// list of the three primary UI fonts. UI Toolkit's TextCore fallback chain
    /// (main font → local fallback list → global list → OS fallback) then renders
    /// Chinese glyphs automatically, so no USS changes are required.
    ///
    /// Idempotent: re-runs reuse existing font assets and only re-verify wiring.
    /// The SDF atlas generation is slow — run as a detached job:
    ///   unity command --detach menu --path "Fodinae/Assets/Create CJK Fallback Fonts"
    /// </summary>
    internal static class CjkFontAssetCreator
    {
        private const string FontsDirectory = "Assets/Resources/Fonts";

        private const string SimplifiedFontPath = FontsDirectory + "/NotoSansSC-Regular.otf";
        private const string TraditionalFontPath = FontsDirectory + "/NotoSansTC-Regular.otf";
        private const string SimplifiedAssetPath = FontsDirectory + "/NotoSansSC_SDF.asset";
        private const string TraditionalAssetPath = FontsDirectory + "/NotoSansTC_SDF.asset";

        private static readonly string[] PrimaryFontAssetPaths =
        [
            FontsDirectory + "/Exo2_SDF.asset",
            FontsDirectory + "/Unbounded_SDF.asset",
            FontsDirectory + "/JetBrainsMono_SDF.asset",
        ];

        // TMP (TMPro) font assets for world-space text: the project's UI fonts
        // are TextCore FontAssets usable only by UI Toolkit, while nameplates and
        // chat need TMPro.TMP_FontAsset. Noto covers Latin/Cyrillic/CJK; the mono
        // JetBrainsMono keeps the intended mono look for Latin/Cyrillic and falls
        // back to the Noto TMP fonts for Chinese glyphs.
        private const string SimplifiedTmpAssetPath = FontsDirectory + "/NotoSansSC_TMP.asset";
        private const string TraditionalTmpAssetPath = FontsDirectory + "/NotoSansTC_TMP.asset";
        private const string JetBrainsMonoFontPath = FontsDirectory + "/JetBrainsMono.ttf";
        private const string JetBrainsMonoTmpAssetPath = FontsDirectory + "/JetBrainsMono_TMP.asset";

        [MenuItem("Fodinae/Assets/Create CJK Fallback Fonts")]
        private static void CreateFromMenu()
        {
            Create();
        }

        [MenuItem("Fodinae/Assets/Create CJK World-Text Fonts (TMP)")]
        private static void CreateWorldTextFontsFromMenu()
        {
            CreateWorldTextFonts();
        }

        public static void Create()
        {
            // The bundled OTFs land in the repo without .meta files; import them
            // (and any pending font assets) before loading.
            AssetDatabase.Refresh();

            FontAsset simplified = CreateOrLoad(SimplifiedFontPath, SimplifiedAssetPath, includeCjk: true);
            FontAsset traditional = CreateOrLoad(TraditionalFontPath, TraditionalAssetPath, includeCjk: true);

            int wired = 0;
            foreach (string path in PrimaryFontAssetPaths)
            {
                FontAsset? primary = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
                if (primary == null)
                {
                    Debug.LogWarning($"[CjkFonts] Primary font asset missing: {path}");
                    continue;
                }

                primary.fallbackFontAssetTable = new List<FontAsset> { simplified, traditional };
                EditorUtility.SetDirty(primary);
                AssetDatabase.SaveAssetIfDirty(primary);
                wired++;
            }

            Debug.Log(
                $"[CjkFonts] CJK fallback ready: {simplified.name} " +
                $"({AtlasTextureCount(simplified)} atlas), {traditional.name} " +
                $"({AtlasTextureCount(traditional)} atlas), wired into {wired} primary fonts.");
        }

        /// <summary>
        /// Builds TMPro TMP_FontAssets (simplified primary + traditional fallback)
        /// for world-space text (nameplates, chat). Independent from the UI Toolkit
        /// pipeline above. Idempotent.
        /// </summary>
        public static void CreateWorldTextFonts()
        {
            AssetDatabase.Refresh();

            TMPro.TMP_FontAsset simplified = CreateOrLoadTmpFont(SimplifiedFontPath, SimplifiedTmpAssetPath, includeCjk: true);
            TMPro.TMP_FontAsset traditional = CreateOrLoadTmpFont(TraditionalFontPath, TraditionalTmpAssetPath, includeCjk: true);

            if (simplified.fallbackFontAssetTable == null || simplified.fallbackFontAssetTable.Count == 0)
            {
                simplified.fallbackFontAssetTable = new List<TMPro.TMP_FontAsset> { traditional };
                EditorUtility.SetDirty(simplified);
                AssetDatabase.SaveAssetIfDirty(simplified);
            }

            // Mono face for nameplates: warm only what JetBrainsMono actually has
            // (Latin/Cyrillic) and point the CJK chain at the Noto TMP fonts.
            TMPro.TMP_FontAsset mono = CreateOrLoadTmpFont(JetBrainsMonoFontPath, JetBrainsMonoTmpAssetPath, includeCjk: false);
            if (mono.fallbackFontAssetTable == null || mono.fallbackFontAssetTable.Count == 0)
            {
                mono.fallbackFontAssetTable = new List<TMPro.TMP_FontAsset> { simplified, traditional };
                EditorUtility.SetDirty(mono);
                AssetDatabase.SaveAssetIfDirty(mono);
            }

            Debug.Log(
                $"[CjkFonts] World-text (TMP) fonts ready: {simplified.name} " +
                $"({AtlasTextureCount(simplified)} atlas), {traditional.name} " +
                $"({AtlasTextureCount(traditional)} atlas), {mono.name} " +
                $"({AtlasTextureCount(mono)} atlas); mono + CJK fallback wired.");
        }

        private static TMPro.TMP_FontAsset CreateOrLoadTmpFont(string fontFilePath, string assetPath, bool includeCjk)
        {
            TMPro.TMP_FontAsset? existing = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            Font? font = AssetDatabase.LoadAssetAtPath<Font>(fontFilePath);
            if (font == null)
            {
                throw new InvalidOperationException($"Required font file is missing: {fontFilePath}");
            }

            var fontAsset = TMPro.TMP_FontAsset.CreateFontAsset(
                font,
                samplingPointSize: 40,
                atlasPadding: 5,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 2048,
                atlasHeight: 2048,
                atlasPopulationMode: TMPro.AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            AssetDatabase.CreateAsset(fontAsset, assetPath);
            AttachTmpAtlasTextures(fontAsset);

            string characterSet = CjkGlyphSetBuilder.BuildCharacterSet(includeCjk);
            if (!fontAsset.TryAddCharacters(characterSet, out string missing, includeFontFeatures: true))
            {
                Debug.LogWarning(
                    $"[CjkFonts] {fontAsset.name} omitted unsupported glyphs: " +
                    $"{CjkGlyphSetBuilder.FormatCodePoints(missing)}");
            }

            AttachTmpAtlasTextures(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        private static FontAsset CreateOrLoad(string fontFilePath, string assetPath, bool includeCjk)
        {
            FontAsset? existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            Font? font = AssetDatabase.LoadAssetAtPath<Font>(fontFilePath);
            if (font == null)
            {
                throw new InvalidOperationException($"Required font file is missing: {fontFilePath}");
            }

            var fontAsset = FontAsset.CreateFontAsset(
                font,
                samplingPointSize: 40,
                atlasPadding: 5,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 2048,
                atlasHeight: 2048,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            AssetDatabase.CreateAsset(fontAsset, assetPath);
            AttachAtlasTextures(fontAsset);

            string characterSet = CjkGlyphSetBuilder.BuildCharacterSet(includeCjk);
            if (!fontAsset.TryAddCharacters(characterSet, out string missing, includeFontFeatures: true))
            {
                Debug.LogWarning(
                    $"[CjkFonts] {fontAsset.name} omitted unsupported glyphs: " +
                    $"{CjkGlyphSetBuilder.FormatCodePoints(missing)}");
            }

            AttachAtlasTextures(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        private static void AttachAtlasTextures(FontAsset fontAsset)
        {
            foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
            {
                AttachAtlas(fontAsset, atlasTexture);
            }
        }

        private static void AttachTmpAtlasTextures(TMPro.TMP_FontAsset fontAsset)
        {
            foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
            {
                AttachAtlas(fontAsset, atlasTexture);
            }
        }

        private static int AtlasTextureCount(FontAsset fontAsset)
        {
            int count = 0;
            foreach (Texture2D _ in fontAsset.atlasTextures)
            {
                count++;
            }

            return count;
        }

        private static int AtlasTextureCount(TMPro.TMP_FontAsset fontAsset)
        {
            int count = 0;
            foreach (Texture2D _ in fontAsset.atlasTextures)
            {
                count++;
            }

            return count;
        }

        private static void AttachAtlas(UnityEngine.Object fontAsset, Texture2D atlasTexture)
        {
            if (!AssetDatabase.Contains(atlasTexture))
            {
                atlasTexture.name = $"{fontAsset.name} Atlas";
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }
        }

    }
}
