#nullable enable

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor;

/// <summary>
/// Turns the final-light highlight roll-off on in the shipped lighting defaults.
/// </summary>
/// <remarks>
/// <c>_enableFinalLightingClamp</c> shipped as 0. With it off nothing bounded the light
/// map at all, and because the Universal2D terrain pass applies no tone mapping, every
/// surface near a light saturated to flat white — the washed-out blob the world was
/// rendering around the player.
/// <para>
/// Turning it on used to mean a hard clamp, which is why it was switched off in the first
/// place: it flattened the core of every light into a plateau. That flag now drives a
/// hue-preserving roll-off instead (see <c>RollOffHighlights</c> in WorldLighting.compute):
/// linear up to a knee, then asymptotic to the white point, so light keeps getting brighter
/// towards a source, never plateaus and never exceeds white.
/// </para>
/// <para>
/// ProjectDefaults.asset is a serialized Unity asset and must be written through the
/// SerializedObject API rather than as text, so this lives behind a menu item.
/// </para>
/// </remarks>
public static class ConfigureLightingExposureDefaults
{
    private const string DefaultsPath = "Assets/Resources/Configuration/ProjectDefaults.asset";
    private const string ClampProperty = "_lighting._enableFinalLightingClamp";
    private const string WhitePointProperty = "_lighting._maximumLightMultiplier";
    private const float WhitePoint = 1f;

    [MenuItem("Fodinae/Migrations/Configure Lighting Exposure Defaults")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Lighting defaults must not be rewritten from Play Mode: the runtime holds a " +
                "loaded copy and the asset write would be reverted or lost on exit.");
        }

        ScriptableObject defaults =
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(DefaultsPath) ??
            throw new InvalidOperationException(
                $"Required project defaults asset is missing at '{DefaultsPath}'.");
        SerializedObject serializedDefaults = new(defaults);
        serializedDefaults.Update();

        SerializedProperty clamp = serializedDefaults.FindProperty(ClampProperty) ??
            throw new InvalidOperationException(
                $"'{DefaultsPath}' has no '{ClampProperty}' property.");
        SerializedProperty whitePoint = serializedDefaults.FindProperty(WhitePointProperty) ??
            throw new InvalidOperationException(
                $"'{DefaultsPath}' has no '{WhitePointProperty}' property.");

        clamp.boolValue = true;
        whitePoint.floatValue = WhitePoint;
        serializedDefaults.ApplyModifiedProperties();
        EditorUtility.SetDirty(defaults);
        AssetDatabase.SaveAssetIfDirty(defaults);

        Debug.Log(
            "[ConfigureLightingExposureDefaults] Highlight roll-off enabled with white point " +
            $"{WhitePoint} in '{DefaultsPath}'.");
    }
}
#endif
