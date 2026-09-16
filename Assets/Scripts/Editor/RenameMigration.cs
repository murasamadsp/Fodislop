#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kern.Editor.Migration;

/// <summary>
/// Re-serializes scenes and assets after the Fodinae → Kern rename so that
/// stale <c>m_EditorClassIdentifier</c> fields (which embed the old assembly
/// name + namespace) are replaced with the current <c>Kern.*</c> identifiers.
///
/// Invoke via Unity menu: <c>Kern / Architecture / Rename Migration</c>.
/// </summary>
public static class RenameMigration
{
    [MenuItem("Kern/Architecture/Rename Migration — Reserialize Scenes & Assets")]
    public static void RunMigration()
    {
        var errors = new List<string>();
        Debug.Log("[Kern.RenameMigration] Starting post-rename reserialization...");

        // 1. Re-save all scenes in Assets/ (skip Packages/ — read-only)
        string[] sceneGUIDs = AssetDatabase.FindAssets("t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
            .ToArray();

        foreach (string scenePath in sceneGUIDs)
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                // Update serialized ManagerBinding strings (e.g. _managerType: "Fodinae.World.MapManager, Fodinae.World")
                UpdateManagerBindingStrings(scene, errors);

                int missingCount = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    ValidateComponents(root, ref missingCount);
                }
                EditorSceneManager.SaveScene(scene, scenePath, false);
                Debug.Log($"[Kern.RenameMigration] Reserialized scene: {scenePath} (missing scripts: {missingCount})");
            }
            catch (Exception ex)
            {
                errors.Add($"Scene {scenePath}: {ex.Message}");
            }
        }

        // 2. Re-save VolumeProfile and other .asset files with Component refs
        string[] assetGuids = AssetDatabase.FindAssets("t:VolumeProfile")
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();

        foreach (string assetPath in assetGuids)
        {
            ReserializeAsset(assetPath, errors);
        }

        // Also re-save .asset files in Assets/ that still contain stale refs
        string[] allAssets = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
            .Where(p => p.EndsWith(".asset", StringComparison.Ordinal))
            .Where(p => !p.StartsWith("Assets/Plugins/FMOD", StringComparison.Ordinal))
            .ToArray();

        foreach (string assetPath in allAssets)
        {
            if (assetGuids.Contains(assetPath))
                continue;

            ReserializeAsset(assetPath, errors);
        }

        // 3. Update PlayerSettings
        try
        {
            PlayerSettings.productName = "Project Kern";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "kern.game");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "kern.game");

            // metroPackageName for Windows / WSA builds
            var wsasm = typeof(PlayerSettings).Assembly.GetType("UnityEditor.PlayerSettings+WSA");
            if (wsasm != null)
            {
                var setMethod = wsasm.GetMethod("SetMetroPackageName",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(string) }, null);
                setMethod?.Invoke(null, new object[] { "kern" });
            }

            Debug.Log("[Kern.RenameMigration] Updated PlayerSettings (productName: Project Kern)");
        }
        catch (Exception ex)
        {
            errors.Add($"PlayerSettings: {ex.Message}");
        }

        // 4. Attempt FMOD source path update via reflection
        try
        {
            UpdateFmodSourcePath();
        }
        catch (Exception ex)
        {
            errors.Add($"FMOD settings: {ex.Message}");
        }

        if (errors.Count > 0)
        {
            string msg = $"[Kern.RenameMigration] {errors.Count} errors:\n{string.Join("\n", errors)}";
            Debug.LogError(msg);
            EditorUtility.DisplayDialog("Rename Migration — Errors", msg, "OK");
        }
        else
        {
            Debug.Log("[Kern.RenameMigration] Migration completed. Re-saving all scenes and assets.");
            EditorUtility.DisplayDialog("Rename Migration",
                "All scenes, assets, and ProjectSettings have been re-serialized with Kern namespace identifiers.\n\n" +
                "Open each scene in the Editor and save it manually if any 'Missing Script' warnings appear.",
                "OK");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void ReserializeAsset(string assetPath, List<string> errors)
    {
        try
        {
            // Skip FMOD cache — third-party, regenerated by FMOD
            if (assetPath.StartsWith("Assets/Plugins/FMOD", StringComparison.Ordinal))
                return;

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return;

            string content = File.ReadAllText(assetPath);
            bool hasStale = content.Contains("Fodinae", StringComparison.Ordinal) ||
                           content.Contains("FODINAE", StringComparison.Ordinal);

            if (hasStale)
            {
                EditorUtility.SetDirty(asset);
                Debug.Log($"[Kern.RenameMigration] Reserialized stale asset: {assetPath}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Asset {assetPath}: {ex.Message}");
        }
    }

    private static void UpdateManagerBindingStrings(Scene scene, List<string> errors)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var comp in root.GetComponentsInChildren<Component>())
            {
                if (comp == null)
                    continue;

                var so = new SerializedObject(comp);
                var bindingsProp = so.FindProperty("_managerBindings");
                if (bindingsProp == null)
                {
                    so.Dispose();
                    continue;
                }

                bool modified = false;
                for (int i = 0; i < bindingsProp.arraySize; i++)
                {
                    var element = bindingsProp.GetArrayElementAtIndex(i);
                    var managerTypeProp = element.FindPropertyRelative("_managerType");
                    if (managerTypeProp?.propertyType == SerializedPropertyType.String &&
                        managerTypeProp.stringValue.Contains("Fodinae", StringComparison.Ordinal))
                    {
                        managerTypeProp.stringValue = managerTypeProp.stringValue
                            .Replace("Fodinae", "Kern", StringComparison.Ordinal);
                        modified = true;
                    }
                }

                if (modified)
                {
                    so.ApplyModifiedProperties();
                    Debug.Log($"[Kern.RenameMigration] Updated ManagerBinding strings on: {comp.name}");
                }

                so.Dispose();
            }
        }
    }

    private static void ValidateComponents(GameObject go, ref int missingCount)
    {
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null)
                missingCount++;
        }
        foreach (Transform child in go.transform)
        {
            ValidateComponents(child.gameObject, ref missingCount);
        }
    }

    private static void UpdateFmodSourcePath()
    {
        string settingsPath = "Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset";
        var settings = AssetDatabase.LoadMainAssetAtPath(settingsPath);
        if (settings == null)
            return;

        SerializedObject so = new SerializedObject(settings);
        bool updated = false;

        var sourceProjectProp = so.FindProperty("sourceProjectPath");
        if (sourceProjectProp != null && sourceProjectProp.stringValue.Contains("Fodinae"))
        {
            sourceProjectProp.stringValue = sourceProjectProp.stringValue
                .Replace("FodinaeAudio", "KernAudio");
            updated = true;
        }

        var sourceBankProp = so.FindProperty("sourceBankPath");
        if (sourceBankProp != null && sourceBankProp.stringValue.Contains("Fodinae"))
        {
            sourceBankProp.stringValue = sourceBankProp.stringValue
                .Replace("FodinaeAudio", "KernAudio");
            updated = true;
        }

        if (updated)
        {
            so.ApplyModifiedProperties();
            Debug.Log("[Kern.RenameMigration] Updated FMOD source paths (FodinaeAudio → KernAudio)");
        }
    }
}
