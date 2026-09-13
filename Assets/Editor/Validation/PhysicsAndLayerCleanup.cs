using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class PhysicsAndLayerCleanup
    {
        [MenuItem("Fodinae/Cleanup Physics and Setup Layers")]
        public static void Cleanup()
        {
            RemoveRigidbody2DFromPlayer();
            DisablePhysics2D();
            SetupLayers();
            Debug.Log("Physics cleanup and layer setup complete");
        }

        private static void RemoveRigidbody2DFromPlayer()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (playerPrefab == null)
            {
                Debug.LogWarning("Player prefab not found at Assets/Prefabs/Player.prefab");
                return;
            }

            var rb = playerPrefab.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Object.DestroyImmediate(rb, true);
                PrefabUtility.SaveAsPrefabAsset(playerPrefab, "Assets/Prefabs/Player.prefab");
                Debug.Log("Removed Rigidbody2D from Player prefab");
            }
            else
            {
                Debug.Log("Player prefab has no Rigidbody2D");
            }
        }

        private static void DisablePhysics2D()
        {
            Physics2D.gravity = Vector2.zero;

            var phys2DSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset")[0];
            var serializedObject = new SerializedObject(phys2DSettings);

            var gravity = serializedObject.FindProperty("m_Gravity");
            if (gravity != null)
            {
                gravity.vector2Value = Vector2.zero;
            }

            var collisionMatrix = serializedObject.FindProperty("m_LayerCollisionMatrix");
            if (collisionMatrix != null && collisionMatrix.stringValue.Length > 0)
            {
                collisionMatrix.stringValue = new string('0', collisionMatrix.stringValue.Length);
            }

            serializedObject.ApplyModifiedProperties();
            Debug.Log("Physics2D disabled: gravity=0, collision matrix cleared");
        }

        private static void SetupLayers()
        {
            EnsureLayer("World", 9);
            EnsureLayer("Entities", 10);
            EnsureLayer("Effects", 11);
        }

        private static void EnsureLayer(string layerName, int layerIndex)
        {
            if (LayerMask.NameToLayer(layerName) != -1)
            {
                Debug.Log($"Layer '{layerName}' already exists");
                return;
            }

            var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var serializedObject = new SerializedObject(tagManager);
            var layers = serializedObject.FindProperty("layers");

            if (layers.GetArrayElementAtIndex(layerIndex).stringValue != "")
            {
                Debug.LogWarning($"Layer slot {layerIndex} is already occupied");
                return;
            }

            layers.GetArrayElementAtIndex(layerIndex).stringValue = layerName;
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"Layer '{layerName}' created at index {layerIndex}");
        }
    }
}
