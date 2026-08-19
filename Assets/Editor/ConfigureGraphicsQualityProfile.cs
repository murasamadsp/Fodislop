#nullable enable

#if UNITY_EDITOR
using System;
using Fodinae.Rendering;
using UnityEditor;

namespace Fodinae.Editor;

public static class ConfigureGraphicsQualityProfile
{
    private const string ProfilePath = "Assets/Resources/GraphicsQualityProfile.asset";

    [InitializeOnLoadMethod]
    private static void ConfigureInvalidProfileAfterReload()
    {
        EditorApplication.delayCall += TryConfigureInvalidProfile;
    }

    [MenuItem("Fodinae/Migrations/Configure Graphics Quality Presets")]
    public static void Configure()
    {
        GraphicsQualityProfile profile =
            AssetDatabase.LoadAssetAtPath<GraphicsQualityProfile>(ProfilePath) ??
            throw new InvalidOperationException(
                $"Required graphics quality profile is missing at '{ProfilePath}'.");
        SerializedObject serializedProfile = new(profile);
        serializedProfile.Update();
        WritePreset(serializedProfile, "_veryLow", 1, 384, 64, 12, 15f, 384, 0.65f, 0, 0);
        WritePreset(serializedProfile, "_low", 1, 512, 128, 20, 20f, 512, 0.8f, 0, 0);
        WritePreset(serializedProfile, "_medium", 2, 768, 256, 28, 24f, 768, 0.9f, 1, 0);
        WritePreset(serializedProfile, "_high", 4, 1536, 512, 40, 60f, 1536, 1f, 1, 0);
        WritePreset(serializedProfile, "_veryHigh", 6, 1792, 768, 52, 60f, 1792, 1f, 1, 0);
        WritePreset(serializedProfile, "_ultra", 8, 2048, 1024, 64, 60f, 2048, 1f, 1, 0);
        serializedProfile.ApplyModifiedProperties();
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
        profile.Validate();
    }

    private static void TryConfigureInvalidProfile()
    {
        GraphicsQualityProfile? profile =
            AssetDatabase.LoadAssetAtPath<GraphicsQualityProfile>(ProfilePath);
        if (profile == null)
        {
            return;
        }

        try
        {
            profile.Validate();
        }
        catch (InvalidOperationException)
        {
            // One-time migration of the serialized profile. Standard presets
            // are immutable runtime data; this only repairs missing/old fields.
            Configure();
        }
    }

    private static void WritePreset(
        SerializedObject profile,
        string propertyName,
        int lightingMinimumPixelsPerCell,
        int lightingMaximumTextureDimension,
        int lightingMaximumLightCount,
        int lightingMaximumRaySteps,
        float lightingUpdatesPerSecond,
        int lightingCascadeAtlasLimit,
        float renderScale,
        int vSyncCount,
        int antiAliasing)
    {
        SerializedProperty preset = profile.FindProperty(propertyName) ??
            throw new InvalidOperationException(
                $"Graphics quality profile has no serialized property '{propertyName}'.");
        SetInteger(preset, "LightingMinimumPixelsPerCell", lightingMinimumPixelsPerCell);
        SetInteger(preset, "LightingMaximumTextureDimension", lightingMaximumTextureDimension);
        SetInteger(preset, "LightingMaximumLightCount", lightingMaximumLightCount);
        SetInteger(preset, "LightingMaximumRaySteps", lightingMaximumRaySteps);
        SetFloat(preset, "LightingUpdatesPerSecond", lightingUpdatesPerSecond);
        SetInteger(preset, "LightingCascadeAtlasLimit", lightingCascadeAtlasLimit);
        SetFloat(preset, "RenderScale", renderScale);
        SetInteger(preset, "VSyncCount", vSyncCount);
        SetInteger(preset, "AntiAliasing", antiAliasing);
    }

    private static void SetInteger(SerializedProperty parent, string name, int value)
    {
        SerializedProperty property = parent.FindPropertyRelative(name) ??
            throw new InvalidOperationException(
                $"Graphics quality setting '{parent.propertyPath}.{name}' is missing.");
        property.intValue = value;
    }

    private static void SetFloat(SerializedProperty parent, string name, float value)
    {
        SerializedProperty property = parent.FindPropertyRelative(name) ??
            throw new InvalidOperationException(
                $"Graphics quality setting '{parent.propertyPath}.{name}' is missing.");
        property.floatValue = value;
    }
}
#endif
