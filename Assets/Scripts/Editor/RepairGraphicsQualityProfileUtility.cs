#nullable enable

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class RepairGraphicsQualityProfileUtility
    {
        private const string ProfilePath = "Assets/Resources/GraphicsQualityProfile.asset";

        [MenuItem("Fodinae/Repair Graphics Quality Profile")]
        public static void RepairProfile()
        {
            Object profile = AssetDatabase.LoadAssetAtPath<Object>(ProfilePath);
            if (profile == null)
            {
                throw new System.InvalidOperationException(
                    $"Graphics quality profile is missing at '{ProfilePath}'.");
            }

            var serializedProfile = new SerializedObject(profile);
            SetQualityTier(serializedProfile, "_low", 1, 512, 128, 20, 20f, 512, 0.8f, 0);
            SetQualityTier(serializedProfile, "_medium", 2, 768, 256, 28, 24f, 768, 0.9f, 1);
            SetQualityTier(serializedProfile, "_high", 4, 1536, 512, 40, 60f, 1536, 1f, 1);
            SetQualityTier(serializedProfile, "_ultra", 8, 2048, 1024, 64, 60f, 2048, 1f, 1);
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static void SetQualityTier(
            SerializedObject serializedProfile,
            string tier,
            int pixelsPerCell,
            int maximumTextureDimension,
            int maximumLightCount,
            int maximumRaySteps,
            float updatesPerSecond,
            int atlasLimit,
            float renderScale,
            int vsyncCount)
        {
            SetInt(serializedProfile, $"{tier}.LightingPixelsPerCell", pixelsPerCell);
            SetInt(serializedProfile, $"{tier}.LightingMaximumTextureDimension", maximumTextureDimension);
            SetInt(serializedProfile, $"{tier}.LightingMaximumLightCount", maximumLightCount);
            SetInt(serializedProfile, $"{tier}.LightingMaximumRaySteps", maximumRaySteps);
            SetFloat(serializedProfile, $"{tier}.LightingUpdatesPerSecond", updatesPerSecond);
            SetInt(serializedProfile, $"{tier}.LightingCascadeAtlasLimit", atlasLimit);
            SetFloat(serializedProfile, $"{tier}.RenderScale", renderScale);
            SetInt(serializedProfile, $"{tier}.VSyncCount", vsyncCount);
            SetInt(serializedProfile, $"{tier}.AntiAliasing", 0);
        }

        private static void SetInt(SerializedObject serializedObject, string path, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(path) ??
                throw new System.InvalidOperationException($"Missing profile field '{path}'.");
            property.intValue = value;
        }

        private static void SetFloat(SerializedObject serializedObject, string path, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(path) ??
                throw new System.InvalidOperationException($"Missing profile field '{path}'.");
            property.floatValue = value;
        }
    }
}
#endif
