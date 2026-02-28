using System;
using UnityEngine;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Game.Managers;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Test script to force terrain visibility and provide visual debugging
    /// </summary>
    public class TerrainVisibilityTest : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private bool _forceTestOnStart = true;
        [SerializeField] private float _testDelay = 2f;
        [SerializeField] private bool _enableVisualDebugging = true;

        private WorldBackgroundRenderer _renderer;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Camera _mainCamera;

        void Start()
        {
            if (_forceTestOnStart)
            {
                Invoke(nameof(RunVisibilityTest), _testDelay);
            }
        }

        private void RunVisibilityTest()
        {
            Debug.Log("=== TERRAIN VISIBILITY TEST STARTED ===");

            _renderer = FindObjectOfType<WorldBackgroundRenderer>();
            if (_renderer == null)
            {
                Debug.LogError("❌ WorldBackgroundRenderer not found!");
                return;
            }

            _meshFilter = _renderer.GetComponent<MeshFilter>();
            _meshRenderer = _renderer.GetComponent<MeshRenderer>();
            _mainCamera = Camera.main;

            if (_meshFilter == null || _meshRenderer == null || _mainCamera == null)
            {
                Debug.LogError("❌ Missing required components!");
                return;
            }

            Debug.Log("✅ Found all required components");

            // Test 1: Check if mesh exists and has data
            TestMeshData();

            // Test 2: Check material and texture
            TestMaterial();

            // Test 3: Check visibility settings
            TestVisibility();

            // Test 4: Force visual debugging if enabled
            if (_enableVisualDebugging)
            {
                EnableVisualDebugging();
            }

            // Test 5: Force a mesh update
            ForceMeshUpdate();

            Debug.Log("=== TERRAIN VISIBILITY TEST COMPLETED ===");
        }

        private void TestMeshData()
        {
            Debug.Log("--- Testing Mesh Data ---");

            if (_meshFilter.mesh == null)
            {
                Debug.LogError("❌ Mesh is null!");
                return;
            }

            var mesh = _meshFilter.mesh;
            Debug.Log($"Mesh vertices: {mesh.vertexCount}");
            Debug.Log($"Mesh triangles: {mesh.triangles.Length}");
            Debug.Log($"Mesh UVs: {mesh.uv.Length}");
            Debug.Log($"Mesh bounds: {mesh.bounds}");

            if (mesh.vertexCount > 0 && mesh.triangles.Length > 0)
            {
                Debug.Log("✅ Mesh has geometry data");
            }
            else
            {
                Debug.LogError("❌ Mesh has no geometry data!");
            }
        }

        private void TestMaterial()
        {
            Debug.Log("--- Testing Material ---");

            if (_meshRenderer.material == null)
            {
                Debug.LogError("❌ Material is null!");
                return;
            }

            var material = _meshRenderer.material;
            Debug.Log($"Material name: {material.name}");
            Debug.Log($"Material shader: {material.shader.name}");
            Debug.Log($"Material texture: {material.mainTexture}");

            if (material.mainTexture != null)
            {
                Debug.Log($"✅ Material has texture: {material.mainTexture.name}");
                Debug.Log($"Texture size: {material.mainTexture.width}x{material.mainTexture.height}");
            }
            else
            {
                Debug.LogError("❌ Material has no texture!");
            }
        }

        private void TestVisibility()
        {
            Debug.Log("--- Testing Visibility ---");

            var meshPos = _renderer.transform.position;
            var cameraPos = _mainCamera.transform.position;
            var cameraBounds = GetCameraBounds();

            Debug.Log($"Mesh position: {meshPos}");
            Debug.Log($"Camera position: {cameraPos}");
            Debug.Log($"Camera bounds: {cameraBounds}");

            // Check if mesh is within camera view
            if (Mathf.Abs(meshPos.z) < 0.1f)
            {
                Debug.Log("✅ Mesh is at Z=0 (visible to camera)");
            }
            else
            {
                Debug.LogError($"❌ Mesh is at Z={meshPos.z} (outside camera view)");
            }

            // Check if mesh is within camera frustum
            if (IsMeshInCameraFrustum(meshPos, cameraBounds))
            {
                Debug.Log("✅ Mesh is within camera frustum");
            }
            else
            {
                Debug.LogError("❌ Mesh is outside camera frustum");
            }

            // Check renderer settings
            Debug.Log($"Renderer enabled: {_meshRenderer.enabled}");
            Debug.Log($"Renderer sorting order: {_meshRenderer.sortingOrder}");
            Debug.Log($"Renderer layer: {_meshRenderer.gameObject.layer}");
        }

        private void EnableVisualDebugging()
        {
            Debug.Log("--- Enabling Visual Debugging ---");

            // Add a wireframe gizmo to make the mesh visible
            var gizmo = _renderer.gameObject.AddComponent<MeshGizmo>();
            gizmo.enabled = true;

            // Force the mesh to be visible by making it wireframe
            if (_meshFilter.mesh != null)
            {
                Debug.Log("Mesh data for debugging:");
                var vertices = _meshFilter.mesh.vertices;
                var triangles = _meshFilter.mesh.triangles;
                var uvs = _meshFilter.mesh.uv;

                Debug.Log($"Vertices count: {vertices.Length}");
                Debug.Log($"Triangles count: {triangles.Length}");
                Debug.Log($"UVs count: {uvs.Length}");

                // Log first few vertices for debugging
                for (int i = 0; i < Mathf.Min(4, vertices.Length); i++)
                {
                    Debug.Log($"Vertex {i}: {vertices[i]}");
                }
            }
        }

        private void ForceMeshUpdate()
        {
            Debug.Log("--- Forcing Mesh Update ---");

            // Try to force the renderer to update
            if (_renderer != null)
            {
                // Force a camera position change to trigger update
                var currentPos = _mainCamera.transform.position;
                _mainCamera.transform.position = new Vector3(currentPos.x + 1, currentPos.y, currentPos.z);
                _mainCamera.transform.position = currentPos;

                Debug.Log("✅ Forced camera position update to trigger mesh regeneration");
            }
        }

        private Bounds GetCameraBounds()
        {
            var camera = _mainCamera;
            var bounds = new Bounds();

            // Calculate camera frustum bounds at Z=0 (where our mesh is)
            var screenCorners = new Vector3[4];
            screenCorners[0] = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
            screenCorners[1] = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));
            screenCorners[2] = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
            screenCorners[3] = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));

            // Create bounds from corners
            bounds.center = (screenCorners[0] + screenCorners[2]) / 2;
            bounds.size = screenCorners[2] - screenCorners[0];

            return bounds;
        }

        private bool IsMeshInCameraFrustum(Vector3 meshPos, Bounds cameraBounds)
        {
            return cameraBounds.Contains(meshPos);
        }

        /// <summary>
        /// Simple gizmo component for visual debugging
        /// </summary>
        private class MeshGizmo : MonoBehaviour
        {
            private MeshFilter _meshFilter;
            private MeshRenderer _meshRenderer;

            void OnDrawGizmos()
            {
                if (!enabled) return;

                _meshFilter = GetComponent<MeshFilter>();
                _meshRenderer = GetComponent<MeshRenderer>();

                if (_meshFilter == null || _meshFilter.mesh == null) return;

                var mesh = _meshFilter.mesh;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;

                Gizmos.color = Color.red;
                Gizmos.matrix = transform.localToWorldMatrix;

                // Draw wireframe of the mesh
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var v1 = vertices[triangles[i]];
                    var v2 = vertices[triangles[i + 1]];
                    var v3 = vertices[triangles[i + 2]];

                    Gizmos.DrawLine(v1, v2);
                    Gizmos.DrawLine(v2, v3);
                    Gizmos.DrawLine(v3, v1);
                }
            }
        }
    }
}