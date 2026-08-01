#nullable enable

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Editor;

[InitializeOnLoad]
internal static class SdrOutputEnforcer
{
    private const string HdrDisplayMigrationKey = "Fodinae.Rendering.HdrDisplayMigration";

    static SdrOutputEnforcer()
    {
        EditorApplication.delayCall += EnsureSdrOutput;
    }

    private static void EnsureSdrOutput()
    {
        bool changed = false;

        var pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
            ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset != null && !pipelineAsset.supportsHDR)
        {
            pipelineAsset.supportsHDR = true;
            EditorUtility.SetDirty(pipelineAsset);
            changed = true;
        }

        UnityEngine.Object[] settingsObjects = Resources.FindObjectsOfTypeAll(typeof(PlayerSettings));
        foreach (UnityEngine.Object settingsObject in settingsObjects)
        {
            var serializedSettings = new SerializedObject(settingsObject);
            SerializedProperty? allowHdr = serializedSettings.FindProperty("allowHDRDisplaySupport");

            if (allowHdr is { boolValue: true })
            {
                allowHdr.boolValue = false;
                changed = true;
            }

            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        if (PlayerSettings.allowHDRDisplaySupport)
        {
            PlayerSettings.allowHDRDisplaySupport = false;
            changed = true;
        }

        if (!SessionState.GetBool(HdrDisplayMigrationKey, false))
        {
            // Force PlayerSettings to dirty this field even if its native value
            // was changed earlier in the editor session but not flushed to disk.
            PlayerSettings.useHDRDisplay = true;
            PlayerSettings.useHDRDisplay = false;
            SessionState.SetBool(HdrDisplayMigrationKey, true);
            changed = true;
        }
        else if (PlayerSettings.useHDRDisplay)
        {
            PlayerSettings.useHDRDisplay = false;
            changed = true;
        }

        if (changed)
        {
            Debug.Log("[Rendering] Internal HDR enabled for lighting; HDR display output disabled for stable SDR presentation.");
        }

        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save Project");
    }
}
