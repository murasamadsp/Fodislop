#nullable enable

using System;
using System.IO;
using Kern.World;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

/// <summary>Batch-safe import and profile creation; never edits scene YAML.</summary>
public static class SurfaceSceneAuthoring
{
    [MenuItem("Kern/World/Author Surface Assets")]
    public static void Author()
    {
        AssetDatabase.Refresh();
        string[] names = new[] {
"ridge-a", "ridge-b", "ridge-c", "basalt", "moon"
 };
        foreach (string name in names)
        {
            string path = $"Assets/Textures/Surface/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter ??
                throw new InvalidOperationException($"Missing surface texture '{path}'.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = name != "basalt";
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = name == "basalt" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = name == "moon" ? 512 : 2048;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        const string Folder = "Assets/Resources/Surface";
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string profilePath = $"{Folder}/SurfaceScene.asset";
        SurfaceSceneProfile? profile = AssetDatabase.LoadAssetAtPath<SurfaceSceneProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<SurfaceSceneProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        var serialized = new SerializedObject(profile);
        serialized.FindProperty("_shader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/World/SurfaceScene.shader");
        serialized.FindProperty("_basalt").objectReferenceValue = Texture("basalt");
        serialized.FindProperty("_moon").objectReferenceValue = Texture("moon");
        SerializedProperty ridges = serialized.FindProperty("_distantRidges");
        ridges.arraySize = 3;
        for (int index = 0; index < 3; index++)
        {
            ridges.GetArrayElementAtIndex(index).objectReferenceValue = Texture(names[index]);
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        profile.Validate();
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[SurfaceScene] Imported five assets and authored Resources/Surface/SurfaceScene.");

        static Texture2D Texture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/Surface/{name}.png") ??
            throw new InvalidOperationException($"Could not import surface texture {name}.");
    }
}
