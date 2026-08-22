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
        WritePreset(serializedProfile, "_veryLow", 1, 384, 32, 8, 60f, 256, 0.75f, 0, 0);
        WritePreset(serializedProfile, "_low", 2, 512, 64, 8, 60f, 384, 0.85f, 0, 0);
        WritePreset(serializedProfile, "_medium", 4, 768, 128, 8, 60f, 512, 1.0f, 0, 0);
        WritePreset(serializedProfile, "_high", 8, 1024, 256, 8, 60f, 768, 1.0f, 0, 0);
        WritePreset(serializedProfile, "_veryHigh", 16, 1280, 512, 8, 60f, 1024, 1.0f, 0, 0);
        WritePreset(serializedProfile, "_ultra", 16, 1536, 1024, 8, 60f, 1536, 1.0f, 0, 0);
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
