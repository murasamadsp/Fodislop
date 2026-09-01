#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Fodinae.Editor;

/// <summary>
/// Rebuilds the project's UI fonts with a deterministic initial glyph
/// set while keeping Dynamic mode required by Unity 6 Advanced Text.
/// Latin fonts cover ASCII + en/ru; the CJK fallback fonts (created by
/// CjkFontAssetCreator) additionally cover the zh/zh-hant dictionaries.
/// </summary>
internal static class StableUiFontAssetNormalizer
{
    private const string MenuPath = "Fodinae/Assets/Normalize Stable UI Fonts";

    private static readonly (string Path, bool IncludeCjk)[] FontAssets =
    [
        ("Assets/Resources/Fonts/Exo2_SDF.asset", false),
        ("Assets/Resources/Fonts/JetBrainsMono_SDF.asset", false),
        ("Assets/Resources/Fonts/Unbounded_SDF.asset", false),
        ("Assets/Resources/Fonts/NotoSansSC_SDF.asset", true),
        ("Assets/Resources/Fonts/NotoSansTC_SDF.asset", true),
    ];

    [MenuItem(MenuPath)]
    private static void NormalizeFromMenu()
    {
        Normalize();
    }

    private static void Normalize()
    {
        foreach ((string assetPath, bool includeCjk) in FontAssets)
        {
            FontAsset? fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
            if (fontAsset == null)
            {
                throw new InvalidOperationException($"Required UI font asset is missing: {assetPath}");
            }

            NormalizeFontAsset(fontAsset, assetPath, CjkGlyphSetBuilder.BuildCharacterSet(includeCjk));
            AssetDatabase.SaveAssetIfDirty(fontAsset);
        }

        Debug.Log("[StableUiFonts] UI fonts were rebuilt in Dynamic atlas mode.");
    }

    private static void NormalizeFontAsset(
        FontAsset fontAsset,
        string assetPath,
        string characterSet)
    {
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.ClearFontAssetData();
        if (!fontAsset.TryAddCharacters(characterSet, out string missingCharacters, includeFontFeatures: true))
        {
            string missingCodePoints = CjkGlyphSetBuilder.FormatCodePoints(missingCharacters);
            Debug.Log(
                $"[StableUiFonts] Font '{assetPath}' omitted unsupported optional glyphs: " +
                $"{missingCodePoints}",
                fontAsset);
        }

        var serializedFont = new SerializedObject(fontAsset);
        SerializedProperty? clearOnBuild = serializedFont.FindProperty("m_ClearDynamicDataOnBuild");
        if (clearOnBuild != null)
        {
            clearOnBuild.boolValue = false;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
        {
            if (!AssetDatabase.Contains(atlasTexture))
            {
                atlasTexture.name = $"{fontAsset.name} Atlas";
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }

            EditorUtility.SetDirty(atlasTexture);
            AssetDatabase.SaveAssetIfDirty(atlasTexture);
        }

        RemoveOrphanedAtlasTextures(fontAsset, assetPath);

        EditorUtility.SetDirty(fontAsset);
    }

    private static void RemoveOrphanedAtlasTextures(FontAsset fontAsset, string assetPath)
    {
        var activeAtlases = new HashSet<Texture2D>(fontAsset.atlasTextures);
        foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (subAsset is Texture2D texture && !activeAtlases.Contains(texture))
            {
                UnityEngine.Object.DestroyImmediate(texture, allowDestroyingAssets: true);
            }
        }
    }

}
