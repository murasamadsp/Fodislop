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
            ApplyLightingInspectorDefaults(engine);

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
            SetFloat(serializedEngine, "_emissionScale", 4f);
            SetFloat(serializedEngine, "_bounceStrength", 0.65f);
            SetFloat(serializedEngine, "_maximumLightMultiplier", 16f);
            SetFloat(serializedEngine, "_ambientIntensity", 0.85f);
            SetFloat(serializedEngine, "_ambientOcclusionStrength", 0.65f);
            SetColor(serializedEngine, "_emptyExtinctionRgb", new Color(0.015f, 0.012f, 0.009f, 1f));
            SetColor(serializedEngine, "_solidExtinctionRgb", new Color(1.2f, 1.1f, 1f, 1f));
            serializedEngine.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(engine);
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
