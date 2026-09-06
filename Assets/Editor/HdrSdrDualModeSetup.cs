#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.EditorTools;

/// <summary>
/// One-way project setup for URP HDR/SDR display switching.
/// Scene rendering remains scene-linear HDR in both modes. URP owns tone
/// mapping and display encoding; Fodinae owns the artistic effects.
///
/// Run from Fodinae/Rendering/Apply HDR-SDR Dual Mode Setup.
/// </summary>
internal static class HdrSdrDualModeSetup
{
    private const string UniversalRPPath = "Assets/Settings/UniversalRP.asset";
    private const string MenuPath = "Fodinae/Rendering/Apply HDR-SDR Dual Mode Setup";

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        try
        {
            ApplyPlayerSettings();
            ApplyUniversalRP();
            RemoveBuiltInTonemapping();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[HdrSdrDualModeSetup] HDR/SDR dual mode configured: " +
                "HDR resources included and Unity tonemapping removed from the custom profile.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[HdrSdrDualModeSetup] Failed: {exception}");
            throw;
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateApply() => !Application.isPlaying;

    private static void ApplyPlayerSettings()
    {
        // Include URP's HDR encoding resources even though the application
        // starts in SDR and opts into HDR later through HDROutputSettings.
        if (!PlayerSettings.allowHDRDisplaySupport)
        {
            PlayerSettings.allowHDRDisplaySupport = true;
            Debug.Log("[HdrSdrDualModeSetup] Enabled PlayerSettings.allowHDRDisplaySupport.");
        }

        // ApplicationBootstrap applies the saved preference before Gateway,
        // so the build default must not override a player who selected SDR.
        PlayerSettings.useHDRDisplay = false;
    }

    private static void ApplyUniversalRP()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UniversalRPPath);
        if (urp == null)
        {
            throw new InvalidOperationException(
                $"Required URP asset was not found at '{UniversalRPPath}'.");
        }

        var serialized = new SerializedObject(urp);
        SerializedProperty supportsHdr = serialized.FindProperty("m_SupportsHDR") ??
            throw new InvalidOperationException(
                $"URP asset '{UniversalRPPath}' does not expose m_SupportsHDR.");
        if (supportsHdr.boolValue)
        {
            return;
        }

        supportsHdr.boolValue = true;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);
        Debug.Log("[HdrSdrDualModeSetup] Enabled URP HDR render targets.");
    }

    private static readonly string[] _CleanProfilePaths =
    [
        "Assets/Settings/PostProcessVolumeProfile.asset",
        "Assets/Settings/DefaultVolumeProfile.asset",
        "Assets/Settings/MenuSceneryVolumeProfile.asset",
    ];

    private static readonly Type[] _BuiltInDuplicateTypes =
    [
        typeof(Tonemapping),
        typeof(LiftGammaGain),
        typeof(ColorAdjustments),
        typeof(ColorCurves),
        typeof(Bloom),
        typeof(Vignette),
        typeof(ChromaticAberration),
        typeof(MotionBlur),
        typeof(FilmGrain),
        typeof(ChannelMixer),
        typeof(SplitToning),
        typeof(WhiteBalance),
        typeof(LensDistortion),
        typeof(PaniniProjection),
        typeof(DepthOfField),
        typeof(ColorLookup),
        typeof(ShadowsMidtonesHighlights),
        typeof(ScreenSpaceLensFlare),
    ];

    [MenuItem("Fodinae/Rendering/Clean Display Volume Profiles")]
    private static void CleanDisplayProfiles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Volume profile cleanup requires Edit Mode.");
        }

        // Validate every target before changing any asset.
        foreach (string path in _CleanProfilePaths)
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) == null)
            {
                throw new InvalidOperationException($"Missing VolumeProfile: {path}");
            }
        }

        RemoveBuiltInTonemapping();
        ValidateDisplayProfiles();
    }

    [MenuItem("Fodinae/Rendering/Validate Display Volume Profiles")]
    private static void ValidateDisplayProfiles()
    {
        foreach (string path in _CleanProfilePaths)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path)
                ?? throw new InvalidOperationException($"Missing VolumeProfile: {path}");
            foreach (VolumeComponent component in profile.components)
            {
                if (component != null && Array.IndexOf(_BuiltInDuplicateTypes, component.GetType()) >= 0)
                {
                    throw new InvalidOperationException($"{path}: unexpected native effect {component.GetType().Name}.");
                }
            }
        }

        Debug.Log("[DisplayProfiles] All three authored profiles are free of native post effects. Output tonemapping belongs to Bootstrap.");
    }

    private static void RemoveBuiltInTonemapping()
    {
        var builtInTypesSet = new HashSet<Type>(_BuiltInDuplicateTypes);
        foreach (string profilePath in _CleanProfilePaths)
        {
            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                continue;
            }

            bool changed = false;
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                VolumeComponent component = profile.components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                if (builtInTypesSet.Contains(type))
                {
                    Debug.Log($"[DisplayProfiles] Removing {type.Name} from {profilePath}.");
                    profile.components.RemoveAt(i);
                    UnityEngine.Object.DestroyImmediate(component, allowDestroyingAssets: true);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                Debug.Log(
                    $"[HdrSdrDualModeSetup] Removed native post effects from '{profilePath}', preserving custom components.");
            }
        }
    }
}
#endif
