#if UNITY_EDITOR
#nullable enable

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kern.Editor;

[InitializeOnLoad]
public static class PlayModeSceneBootstrapper
{
    public const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    public const string TargetSceneSessionKey = "Kern.PlayModeTargetScene";

    static PlayModeSceneBootstrapper()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EnsurePlayModeStartScene();
    }

    [MenuItem("Kern/Architecture/Ensure Play Mode Bootstrap Scene")]
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
            targetScene != "Bootstrap")
        {
            SessionState.SetString(TargetSceneSessionKey, targetScene);
            Debug.Log($"[PlayModeSceneBootstrapper] Play mode requested with scene '{targetScene}' active/selected; launching Bootstrap first.");
        }
        else
        {
            SessionState.SetString(TargetSceneSessionKey, string.Empty);
        }
    }
}
#endif
