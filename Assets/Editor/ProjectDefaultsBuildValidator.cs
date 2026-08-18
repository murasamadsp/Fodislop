#nullable enable

using System;
using Fodinae.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Fodinae.Editor;

public sealed class ProjectDefaultsBuildValidator : IPreprocessBuildWithReport
{
    private const string RequiredAssetPath =
        "Assets/Resources/Configuration/ProjectDefaults.asset";

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        ValidateRequiredAsset();
    }

    [MenuItem("Fodinae/Diagnostics/Validate Project Defaults")]
    public static void ValidateRequiredAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:ProjectDefaults");
        if (guids.Length != 1)
        {
            throw new BuildFailedException(
                $"Exactly one ProjectDefaults asset must exist; found {guids.Length}.");
        }

        string actualPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        if (!string.Equals(actualPath, RequiredAssetPath, StringComparison.Ordinal))
        {
            throw new BuildFailedException(
                $"ProjectDefaults asset must be located at '{RequiredAssetPath}', " +
                $"but was found at '{actualPath}'.");
        }

        ProjectDefaults defaults =
            AssetDatabase.LoadAssetAtPath<ProjectDefaults>(RequiredAssetPath) ??
            throw new BuildFailedException(
                $"ProjectDefaults could not be loaded from '{RequiredAssetPath}'.");

        defaults.Validate();
    }
}
