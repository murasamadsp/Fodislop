#nullable enable

#if UNITY_EDITOR
using Fodinae.Rendering;
using Fodinae.World.Lighting;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    [CustomEditor(typeof(TerrariaLightingEngine))]
    public sealed class TerrariaLightingEngineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_graphicsProfile");
            SerializedProperty profileProperty = serializedObject.FindProperty("_graphicsProfile")!;
            EditorGUILayout.PropertyField(profileProperty);
            serializedObject.ApplyModifiedProperties();

            Object? profileObject = profileProperty.objectReferenceValue;
            if (profileObject == null)
            {
                EditorGUILayout.HelpBox(
                    "GraphicsQualityProfile is required: its fields drive cascade size, ray steps, " +
                    "light count and the base extinction values sent to WorldLighting.compute.",
                    MessageType.Error);
                DrawActualShaderUniforms((TerrariaLightingEngine)target);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shader quality uniforms", EditorStyles.boldLabel);
            SerializedObject profile = new(profileObject);
            profile.Update();
            DrawProfileTier(profile, "_low", "Low");
            DrawProfileTier(profile, "_medium", "Medium");
            DrawProfileTier(profile, "_high", "High");
            DrawProfileTier(profile, "_ultra", "Ultra");
            profile.ApplyModifiedProperties();

            DrawActualShaderUniforms((TerrariaLightingEngine)target);
        }

        private static void DrawActualShaderUniforms(TerrariaLightingEngine engine)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actual WorldLighting.compute uniforms", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These are the values currently sent to the compute shader. Derived values are read-only.",
                MessageType.Info);
            EditorGUILayout.Vector2IntField("_FieldSize", new(engine.FieldWidth, engine.FieldHeight));
            EditorGUILayout.Vector2IntField("_BounceSize", new(engine.BounceWidth, engine.BounceHeight));
            EditorGUILayout.Vector4Field("_WorldRect", engine.WorldRect);
            EditorGUILayout.ColorField(new GUIContent("_AmbientColor"), engine.ComputeAmbientColor, true, true, true);
            EditorGUILayout.ColorField(new GUIContent("_EmptyExtinctionRgb"), engine.ComputeEmptyExtinction, true, true, true);
            EditorGUILayout.ColorField(new GUIContent("_SolidExtinctionRgb"), engine.ComputeSolidExtinction, true, true, true);
            EditorGUILayout.FloatField("_MinimumTransmission", engine.MinimumTransmission);
            EditorGUILayout.FloatField("_BounceStrength", engine.BounceStrength);
            EditorGUILayout.FloatField("_EmissionScale", engine.EmissionScale);
            EditorGUILayout.FloatField("_MaximumLightMultiplier", engine.MaximumLightMultiplier);
            EditorGUILayout.FloatField("_CellSize", engine.CellSize);
            EditorGUILayout.FloatField("_AmbientOcclusionRadiusCells", engine.AmbientOcclusionRadiusCells);
            EditorGUILayout.FloatField("_AmbientOcclusionStrength", engine.AmbientOcclusionStrength);
            EditorGUILayout.FloatField("_TransmittanceDebugDistanceCells", engine.TransmittanceDebugDistanceCells);
            EditorGUILayout.EnumPopup("_DebugView", engine.ActiveDebugView);
            EditorGUILayout.IntField("_MaterialYFlip", engine.MaterialYFlip);
            EditorGUILayout.IntField("_MaximumIntervalSteps", engine.MaximumIntervalSteps);
            EditorGUILayout.IntField("_EnableContactOcclusion", engine.AmbientOcclusionEnabled ? 1 : 0);
            EditorGUILayout.IntField("_EnableDiffuseBounce", engine.DiffuseBounceEnabled ? 1 : 0);
            EditorGUILayout.IntField("Cascade count", engine.CascadeCount);
            foreach (string summary in engine.GetCascadeUniformSummaries())
            {
                EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
            }
        }

        private static void DrawProfileTier(
            SerializedObject profile,
            string propertyName,
            string label)
        {
            SerializedProperty? property = profile.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            string[] qualityProperties =
            [
                "LightingPixelsPerCell",
                "LightingMaximumTextureDimension",
                "LightingMaximumLightCount",
                "LightingMaximumRaySteps",
                "LightingUpdatesPerSecond",
                "LightingCascadeAtlasLimit",
                "RenderScale",
                "VSyncCount",
                "AntiAliasing",
            ];
            foreach (string childName in qualityProperties)
            {
                SerializedProperty? child = property.FindPropertyRelative(childName);
                if (child != null)
                {
                    EditorGUILayout.PropertyField(child);
                }
            }

            EditorGUI.indentLevel--;
        }
    }
}
#endif
