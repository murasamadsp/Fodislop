#nullable enable

#if UNITY_EDITOR
using System.Linq;
using Fodinae.Rendering.PostProcessing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Editor;

public static class FixRenderer2DFeaturesUtility
{
    private const string RendererDataPath = "Assets/Settings/Renderer2D.asset";
    private const string PostProcessScriptPath =
        "Assets/Scripts/Rendering/PostProcessing/PostProcessRendererFeature.cs";

    [MenuItem("Fodinae/Rendering/Attach Post Process Renderer Feature")]
    public static void AttachPostProcessRendererFeature()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[PostProcess] Exit Play Mode before modifying Renderer2D.asset.");
            return;
        }

        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererDataPath);
        if (rendererData == null)
        {
            Debug.LogError($"[PostProcess] Renderer data not found at {RendererDataPath}.");
            return;
        }

        RepairScriptLinks();

        var feature = AssetDatabase
            .LoadAllAssetsAtPath(RendererDataPath)
            .OfType<PostProcessRendererFeature>()
            .FirstOrDefault(candidate => rendererData.rendererFeatures.Contains(candidate));

        if (feature == null)
        {
            Debug.LogError(
                $"[PostProcess] Could not resolve an existing {nameof(PostProcessRendererFeature)} subasset after repairing its script link.");
            return;
        }

        if (!rendererData.rendererFeatures.Contains(feature))
        {
            Undo.RecordObject(rendererData, "Attach Post Process Renderer Feature");
            rendererData.rendererFeatures.Add(feature);
        }

        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();

        Selection.activeObject = rendererData;
        Debug.Log($"[PostProcess] {nameof(PostProcessRendererFeature)} is attached to {RendererDataPath}.", rendererData);
    }

    [MenuItem("Fodinae/Rendering/Repair Renderer Feature Script Links")]
    public static void RepairScriptLinks()
    {
        RepairScriptLink(nameof(PostProcessRendererFeature), PostProcessScriptPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(RendererDataPath, ImportAssetOptions.ForceUpdate);
    }

    private static void RepairScriptLink(string featureName, string scriptPath)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null || script.GetClass() == null)
        {
            Debug.LogError($"[PostProcess] Valid MonoScript not found at {scriptPath}.");
            return;
        }

        var matches = AssetDatabase
            .LoadAllAssetsAtPath(RendererDataPath)
            .Where(asset => asset != null && asset.name == featureName)
            .ToArray();

        foreach (var asset in matches)
        {
            var serializedAsset = new SerializedObject(asset);
            var scriptProperty = serializedAsset.FindProperty("m_Script");
            if (scriptProperty == null)
            {
                Debug.LogError($"[PostProcess] m_Script property is unavailable for {featureName}.", asset);
                continue;
            }

            var classIdentifierProperty = serializedAsset.FindProperty("m_EditorClassIdentifier");
            var scriptIsCorrect = scriptProperty.objectReferenceValue == script;
            var classIdentifierIsEmpty =
                classIdentifierProperty == null || string.IsNullOrEmpty(classIdentifierProperty.stringValue);
            if (scriptIsCorrect && classIdentifierIsEmpty)
            {
                continue;
            }

            Undo.RecordObject(asset, $"Repair {featureName} Script Link");
            scriptProperty.objectReferenceValue = script;

            if (classIdentifierProperty != null)
            {
                classIdentifierProperty.stringValue = string.Empty;
            }

            serializedAsset.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PostProcess] Repaired m_Script for {featureName} using {scriptPath}.");
        }
    }
}
#endif
