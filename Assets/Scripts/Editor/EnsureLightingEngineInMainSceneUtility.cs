#nullable enable

#if UNITY_EDITOR
using System;
using Fodinae.Rendering;
using Fodinae.World.Lighting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor
{
    public static class EnsureLightingEngineInMainSceneUtility
    {
        private const string MainScenePath = "Assets/Scenes/MainGame.unity";
        private const string GameObjectName = "WorldLighting";
        private const string GraphicsProfilePath = "Assets/Resources/GraphicsQualityProfile.asset";

        [MenuItem("Fodinae/Ensure Lighting Engine In Main Scene")]
        public static void EnsureLightingEngineInMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            TerrariaLightingEngine[] engines = UnityEngine.Object.FindObjectsByType<TerrariaLightingEngine>(
                FindObjectsInactive.Include);

            TerrariaLightingEngine engine = engines.Length switch
            {
                0 => CreateLightingEngine(scene),
                1 => engines[0],
                _ => throw new InvalidOperationException(
                    $"Main scene contains {engines.Length} TerrariaLightingEngine components; expected one."),
            };

            if (engine.gameObject.name != GameObjectName)
            {
                engine.gameObject.name = GameObjectName;
                EditorUtility.SetDirty(engine.gameObject);
            }

            AssignGraphicsProfile(engine);
            EditorUtility.SetDirty(engine);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException($"Could not save scene '{MainScenePath}'.");
            }

            Debug.Log(
                $"[LightingSetup] Inspector settings are available on '{GameObjectName}' " +
                $"({nameof(TerrariaLightingEngine)}). Quality={engine.Quality}, " +
                $"AO={engine.AmbientOcclusionEnabled}, AO radius={engine.AmbientOcclusionRadiusCells}, " +
                $"AO strength={engine.AmbientOcclusionStrength}, ambient={engine.AmbientIntensity}, " +
                $"emission={engine.EmissionScale}.");
        }

        [MenuItem("Fodinae/Reset Lighting Inspector Defaults")]
        public static void ResetLightingInspectorDefaults()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            TerrariaLightingEngine[] engines = UnityEngine.Object.FindObjectsByType<TerrariaLightingEngine>(
                FindObjectsInactive.Include);
            if (engines.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Main scene contains {engines.Length} TerrariaLightingEngine components; expected one.");
            }

            ApplyLightingInspectorDefaults(engines[0]);
            EditorUtility.SetDirty(engines[0]);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException($"Could not save scene '{MainScenePath}'.");
            }

            Debug.Log("[LightingSetup] Reset Inspector defaults from LightingDefaults.");
        }

        private static TerrariaLightingEngine CreateLightingEngine(Scene scene)
        {
            GameObject gameObject = new(GameObjectName);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            TerrariaLightingEngine engine = gameObject.AddComponent<TerrariaLightingEngine>();
            Debug.Log("[LightingSetup] Created WorldLighting with TerrariaLightingEngine in MainGame.");
            return engine;
        }

        private static void AssignGraphicsProfile(TerrariaLightingEngine engine)
        {
            GraphicsQualityProfile profile = AssetDatabase.LoadAssetAtPath<GraphicsQualityProfile>(
                GraphicsProfilePath) ??
                throw new InvalidOperationException(
                    $"Graphics quality profile is missing at '{GraphicsProfilePath}'.");
            SerializedObject serializedEngine = new(engine);
            SerializedProperty profileProperty = serializedEngine.FindProperty("_graphicsProfile") ??
                throw new InvalidOperationException(
                    "TerrariaLightingEngine is missing the serialized _graphicsProfile field.");
            if (profileProperty.objectReferenceValue != profile)
            {
                profileProperty.objectReferenceValue = profile;
                serializedEngine.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(engine);
            }
        }

        private static void ApplyLightingInspectorDefaults(TerrariaLightingEngine engine)
        {
            SerializedObject serializedEngine = new(engine);
            SetEnum(serializedEngine, "_quality", (int)LightingDefaults.Quality);
            SetBool(serializedEngine, "_ambientOcclusionEnabled", LightingDefaults.AmbientOcclusionEnabled);
            SetBool(serializedEngine, "_diffuseBounceEnabled", LightingDefaults.DiffuseBounceEnabled);
            SetFloat(serializedEngine, "_ambientIntensity", LightingDefaults.AmbientIntensity);
            SetFloat(serializedEngine, "_emissionScale", LightingDefaults.EmissionScale);
            SetFloat(serializedEngine, "_emptyExtinctionMultiplier", LightingDefaults.EmptyExtinctionMultiplier);
            SetFloat(serializedEngine, "_solidExtinctionMultiplier", LightingDefaults.SolidExtinctionMultiplier);
            SetFloat(serializedEngine, "_bounceStrength", LightingDefaults.BounceStrength);
            SetFloat(serializedEngine, "_ambientOcclusionRadiusCells", LightingDefaults.AmbientOcclusionRadiusCells);
            SetFloat(serializedEngine, "_ambientOcclusionStrength", LightingDefaults.AmbientOcclusionStrength);
            SetFloat(serializedEngine, "_maximumLightMultiplier", LightingDefaults.MaximumLightMultiplier);
            SetFloat(serializedEngine, "_transmittanceDebugDistanceCells", LightingDefaults.TransmittanceDebugDistanceCells);
            SetFloat(serializedEngine, "_minimumTransmission", LightingDefaults.MinimumTransmission);
            SetInt(serializedEngine, "_lightSafeBorder", LightingDefaults.LightSafeBorder);
            SetBool(serializedEngine, "_enableFinalLightingClamp", LightingDefaults.EnableFinalLightingClamp);
            SetColor(serializedEngine, "_ambientColor", LightingDefaults.AmbientColor);
            SetColor(serializedEngine, "_emptyExtinctionRgb", LightingDefaults.EmptyExtinctionRgb);
            SetColor(serializedEngine, "_solidExtinctionRgb", LightingDefaults.SolidExtinctionRgb);
            serializedEngine.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(engine);
        }

        private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName) ??
                throw new InvalidOperationException($"TerrariaLightingEngine is missing serialized field '{propertyName}'.");
            property.boolValue = value;
        }

        private static void SetEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            SetInt(serializedObject, propertyName, value);
        }

        private static void SetFloat(
            SerializedObject serializedObject,
            string propertyName,
            float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName) ??
                throw new InvalidOperationException(
                    $"TerrariaLightingEngine is missing serialized field '{propertyName}'.");
            property.floatValue = value;
        }

        private static void SetInt(
            SerializedObject serializedObject,
            string propertyName,
            int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName) ??
                throw new InvalidOperationException(
                    $"TerrariaLightingEngine is missing serialized field '{propertyName}'.");
            property.intValue = value;
        }

        private static void SetColor(
            SerializedObject serializedObject,
            string propertyName,
            Color value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName) ??
                throw new InvalidOperationException(
                    $"TerrariaLightingEngine is missing serialized field '{propertyName}'.");
            property.colorValue = value;
        }
    }
}
#endif
