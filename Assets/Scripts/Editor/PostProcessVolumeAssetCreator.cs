#if UNITY_EDITOR
#pragma warning disable CS8632
using System;
using System.Linq;
using Fodinae.Rendering.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Editor;

public static class PostProcessVolumeAssetCreator
{
    private const string ProfileAssetPath = "Assets/Settings/PostProcessVolumeProfile.asset";
    private const string RendererDataPath = "Assets/Settings/Renderer2D.asset";
    private const string ComputeShaderPath = "Assets/Resources/Shaders/PostProcessing/PostProcess.compute";

    [InitializeOnLoadMethod]
    private static void ScheduleDuplicateRendererFeatureRepair()
    {
        EditorApplication.delayCall += RemoveDuplicatePostProcessFeatures;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        RemoveDuplicatePostProcessFeatures();
    }

    private static void RemoveDuplicatePostProcessFeatures()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererDataPath);
        if (rendererData == null)
        {
            return;
        }

        var features = rendererData.rendererFeatures;
        var postProcessIndices = Enumerable.Range(0, features.Count)
            .Where(index => features[index] is PostProcessRendererFeature)
            .ToArray();
        if (postProcessIndices.Length <= 1)
        {
            return;
        }

        int keepIndex = postProcessIndices.FirstOrDefault(index =>
        {
            var serializedFeature = new SerializedObject(features[index]);
            return serializedFeature.FindProperty("_settings")?
                       .FindPropertyRelative("_computeShader")?
                       .objectReferenceValue != null;
        });
        if (!postProcessIndices.Contains(keepIndex))
        {
            keepIndex = postProcessIndices[0];
        }

        Undo.RecordObject(rendererData, "Remove Duplicate Post Process Features");
        for (int i = postProcessIndices.Length - 1; i >= 0; i--)
        {
            int index = postProcessIndices[i];
            if (index == keepIndex)
            {
                continue;
            }

            var duplicate = features[index];
            features.RemoveAt(index);
            if (duplicate != null)
            {
                UnityEngine.Object.DestroyImmediate(duplicate, true);
            }
        }

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PostProcess] Removed {postProcessIndices.Length - 1} duplicate renderer feature(s).");
    }

    [MenuItem("Fodinae/Post-Processing/Create PostProcessVolumeProfile")]
    public static void CreateOrRepairProfile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[PostProcess] Exit Play Mode before modifying the persistent Volume Profile.");
            return;
        }

        CreateOrRepairProfileAsset();
    }

    [MenuItem("Fodinae/Post-Processing/Setup Complete Post Processing")]
    public static void SetupCompletePostProcessing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[PostProcess] Exit Play Mode before modifying renderer, profile, or scene assets.");
            return;
        }

        var profile = CreateOrRepairProfileAsset();
        AssignComputeShaderToRendererFeature();
        EnsureGlobalVolume(profile);

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
        Debug.Log(
            $"[PostProcess] Complete setup finished. Renderer feature uses {ComputeShaderPath}; " +
            $"the active scene uses {ProfileAssetPath}.");
    }

    private static VolumeProfile CreateOrRepairProfileAsset()
    {
        var directory = System.IO.Path.GetDirectoryName(ProfileAssetPath);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory!);
        }

        VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfileAssetPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfileAssetPath);
        }

        EnsureComponent<BloomComponent>(profile, comp =>
        {
            comp.intensity.value = 1.0f;
            comp.threshold.value = 0.8f;
            comp.scatter.value = 0.7f;
            comp.tint.value = Color.white;
        });

        EnsureComponent<VignetteComponent>(profile, comp =>
        {
            comp.intensity.value = 0.25f;
            comp.smoothness.value = 0.35f;
            comp.center.value = new Vector2(0.5f, 0.5f);
            comp.color.value = Color.black;
        });

        EnsureComponent<ChromaticAberrationComponent>(profile, comp =>
        {
            comp.intensity.value = 0.05f;
        });

        EnsureComponent<ColorGradingComponent>(profile, comp =>
        {
            comp.exposure.value = 0f;
            comp.colorFilter.value = Color.white;
            comp.contrast.value = 0f;
            comp.saturation.value = 1f;
        });

        EnsureComponent<EigengrauComponent>(profile, ConfigureEigengrauDefaults, comp =>
        {
            if (comp.noiseScale.value > 2f)
            {
                comp.noiseScale.value = 1f;
            }

            if (comp.animationSpeed.value < 1f)
            {
                comp.animationSpeed.value = 18f;
            }
        });

        EnsureComponent<MotionBlurComponent>(profile, comp =>
        {
            comp.intensity.value = 0.0f;
            comp.maxSamples.value = 8;
        });

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PostProcessVolumeAssetCreator] Created or repaired: " + ProfileAssetPath);
        return profile;
    }

    [MenuItem("Fodinae/Post-Processing/Disable All Effects")]
    public static void DisableAllEffects()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfileAssetPath);
        if (profile == null)
        {
            Debug.LogError($"[PostProcess] Volume Profile not found at {ProfileAssetPath}.");
            return;
        }

        foreach (var component in profile.components)
        {
            if (component == null)
            {
                continue;
            }

            Undo.RecordObject(component, "Disable Post Process Effect");
            component.active = false;
            EditorUtility.SetDirty(component);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[PostProcess] All custom Volume effects disabled.");
    }

    [MenuItem("Fodinae/Post-Processing/Reset Eigengrau Defaults")]
    public static void ResetEigengrauDefaults()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfileAssetPath);
        if (profile == null || !profile.TryGet(out EigengrauComponent? eigengrau) || eigengrau == null)
        {
            Debug.LogError($"[PostProcess] Eigengrau component not found in {ProfileAssetPath}.");
            return;
        }

        Undo.RecordObject(eigengrau, "Reset Eigengrau Defaults");
        ConfigureEigengrauDefaults(eigengrau);
        eigengrau.SetAllOverridesTo(true);
        eigengrau.active = false;
        EditorUtility.SetDirty(eigengrau);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[PostProcess] Eigengrau defaults reset; the effect remains disabled.");
    }

    private static void ConfigureEigengrauDefaults(EigengrauComponent component)
    {
        component.intensity.value = 0.2f;
        component.color.value = new Color(0.018f, 0.02f, 0.028f, 1f);
        component.darknessThreshold.value = 0.18f;
        component.noiseScale.value = 1f;
        component.animationSpeed.value = 18f;
    }

    private static void EnsureComponent<T>(
        VolumeProfile profile,
        Action<T> configureNew,
        Action<T>? repairExisting = null)
        where T : VolumeComponent
    {
        var created = false;
        if (!profile.TryGet(out T? comp) || comp == null)
        {
            comp = profile.Add<T>();
            created = true;
        }

        if (!AssetDatabase.Contains(comp))
        {
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }

        if (created)
        {
            configureNew(comp);
            comp.SetAllOverridesTo(true);
        }
        else
        {
            repairExisting?.Invoke(comp);
        }

        EditorUtility.SetDirty(comp);
    }

    private static void AssignComputeShaderToRendererFeature()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererDataPath);
        var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
        if (rendererData == null || computeShader == null)
        {
            throw new InvalidOperationException(
                $"Renderer data or compute shader is missing: {RendererDataPath}, {ComputeShaderPath}");
        }

        var features = rendererData.rendererFeatures
            .OfType<PostProcessRendererFeature>()
            .ToArray();
        if (features.Length == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PostProcessRendererFeature)} is not attached to {RendererDataPath}.");
        }

        foreach (var feature in features)
        {
            Undo.RecordObject(feature, "Assign Post Process Compute Shader");
            var serializedFeature = new SerializedObject(feature);
            var computeShaderProperty = serializedFeature
                .FindProperty("_settings")?
                .FindPropertyRelative("_computeShader");
            if (computeShaderProperty == null)
            {
                throw new InvalidOperationException(
                    $"Could not find the serialized compute shader field on {nameof(PostProcessRendererFeature)}.");
            }

            computeShaderProperty.objectReferenceValue = computeShader;
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
        }

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
    }

    private static void EnsureGlobalVolume(VolumeProfile profile)
    {
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            throw new InvalidOperationException("No valid active scene is loaded.");
        }

        var volume = UnityEngine.Object
            .FindObjectsByType<Volume>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == activeScene && candidate.isGlobal);

        if (volume == null)
        {
            var volumeObject = new GameObject("GlobalPostProcessVolume");
            Undo.RegisterCreatedObjectUndo(volumeObject, "Create Global Post Process Volume");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(volumeObject, activeScene);
            volume = Undo.AddComponent<Volume>(volumeObject);
        }

        Undo.RecordObject(volume, "Configure Global Post Process Volume");
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
    }
}
#endif
