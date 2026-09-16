#nullable enable

using Kern.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kern.Editor;

[InitializeOnLoad]
public static class PlayModeSceneBootstrapper
{
    public static readonly string BootstrapScenePath =
        BuildSceneOrder.ScenePath(ProjectRuntimeContracts.SceneNames.Bootstrap);

    static PlayModeSceneBootstrapper()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EnsurePlayModeStartScene();
    }

    public static void EnsurePlayModeStartScene()
    {
        SceneAsset? bootstrapAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
        if (bootstrapAsset != null)
        {
            if (EditorSceneManager.playModeStartScene != bootstrapAsset)
            {
                EditorSceneManager.playModeStartScene = bootstrapAsset;
                Debug.Log($"[PlayModeSceneBootstrapper] Play mode start scene configured to '{BootstrapScenePath}'.");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayModeSceneBootstrapper] Bootstrap scene not found at '{BootstrapScenePath}'.");
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
    {
        if (stateChange == PlayModeStateChange.ExitingEditMode)
        {
            EnsurePlayModeStartScene();
            CaptureSelectedTargetScene();
        }
    }

    private static void CaptureSelectedTargetScene()
    {
        string? targetScene = null;
        if (Selection.activeObject is SceneAsset selectedSceneAsset)
        {
            targetScene = selectedSceneAsset.name;
        }
        else
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                targetScene = activeScene.name;
            }
        }

        if (!string.IsNullOrEmpty(targetScene) &&
            targetScene != ProjectRuntimeContracts.SceneNames.Bootstrap)
        {
            SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, targetScene);
            Debug.Log($"[PlayModeSceneBootstrapper] Play mode requested with scene '{targetScene}' active/selected; launching Bootstrap first.");
        }
        else
        {
            SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, string.Empty);
        }
    }
}
