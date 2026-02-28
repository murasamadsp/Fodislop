using System;
using UnityEngine;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Game.Managers;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Simple verification script to test if terrain rendering is working
    /// </summary>
    public class TerrainRenderingVerification : MonoBehaviour
    {
        [Header("Verification Settings")]
        [SerializeField] private bool _autoVerifyOnStart = true;
        [SerializeField] private float _verificationDelay = 1f;

        private WorldBackgroundRenderer _renderer;
        private bool _verificationComplete = false;

        void Start()
        {
            if (_autoVerifyOnStart)
            {
                Invoke(nameof(VerifyTerrainRendering), _verificationDelay);
            }
        }

        private void VerifyTerrainRendering()
        {
            if (_verificationComplete) return;
            _verificationComplete = true;

            Debug.Log("=== TERRAIN RENDERING VERIFICATION ===");

            // Find the terrain renderer
            _renderer = FindObjectOfType<WorldBackgroundRenderer>();
            if (_renderer == null)
            {
                Debug.LogError("❌ WorldBackgroundRenderer not found in scene!");
                return;
            }

            Debug.Log("✅ WorldBackgroundRenderer found");

            // Check if properly configured
            if (_renderer.IsProperlyConfigured())
            {
                Debug.Log("✅ WorldBackgroundRenderer is properly configured");
            }
            else
            {
                Debug.LogError("❌ WorldBackgroundRenderer is not properly configured");
            }

            // Check renderer state
            var state = _renderer.GetRendererState();
            Debug.Log($"Renderer state: {state}");

            if (state == "ReadyForRendering" || state == "Rendering")
            {
                Debug.Log("✅ Renderer is in a ready state");
            }
            else
            {
                Debug.LogWarning($"⚠️  Renderer is in state: {state}");
            }

            // Check visible chunks
            var visibleChunks = _renderer.GetVisibleChunkCount();
            Debug.Log($"Visible chunks: {visibleChunks}");

            if (visibleChunks > 0)
            {
                Debug.Log("✅ Terrain chunks are being generated");
            }
            else
            {
                Debug.LogWarning("⚠️  No visible chunks found - check camera position and render distance");
            }

            // Check textures
            if (_renderer.AreTexturesLoaded())
            {
                Debug.Log("✅ Textures are loaded");
            }
            else
            {
                Debug.LogWarning("⚠️  Textures may not be loaded yet");
            }

            if (_renderer.IsAtlasApplied())
            {
                Debug.Log("✅ Atlas texture is applied");
            }
            else
            {
                Debug.LogWarning("⚠️  Atlas texture may not be applied yet");
            }

            // Check mesh
            var meshFilter = _renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                var mesh = meshFilter.mesh;
                Debug.Log($"Mesh vertices: {mesh.vertexCount}, triangles: {mesh.triangles.Length}");
                
                if (mesh.vertexCount > 0 && mesh.triangles.Length > 0)
                {
                    Debug.Log("✅ Mesh has geometry");
                }
                else
                {
                    Debug.LogWarning("⚠️  Mesh appears to be empty");
                }
            }
            else
            {
                Debug.LogWarning("⚠️  No mesh found on renderer");
            }

            // Check position
            var position = _renderer.transform.position;
            Debug.Log($"Renderer position: {position}");
            
            if (Mathf.Abs(position.z) < 0.1f)
            {
                Debug.Log("✅ Renderer is at Z=0 (visible to camera)");
            }
            else
            {
                Debug.LogError($"❌ Renderer is at Z={position.z} (may be outside camera view)");
            }

            // Check material
            var meshRenderer = _renderer.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.material != null)
            {
                var material = meshRenderer.material;
                Debug.Log($"Material shader: {material.shader.name}");
                Debug.Log($"Material texture: {material.mainTexture}");
                
                if (material.mainTexture != null)
                {
                    Debug.Log("✅ Material has texture assigned");
                }
                else
                {
                    Debug.LogWarning("⚠️  Material has no texture assigned");
                }
            }
            else
            {
                Debug.LogWarning("⚠️  No material found on renderer");
            }

            // Test world data access
            if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
            {
                try
                {
                    var cell = MapStorage.Instance.GetCell(0, 0);
                    Debug.Log($"Test cell at (0,0): {cell}");
                    
                    if (cell != CellType.Unloaded)
                    {
                        Debug.Log("✅ World data is accessible");
                    }
                    else
                    {
                        Debug.LogWarning("⚠️  World data may not be populated");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"❌ Error accessing world data: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("⚠️  MapStorage not ready or not found");
            }

            Debug.Log("=== VERIFICATION COMPLETE ===");

            // If everything looks good, terrain should be visible
            if (state == "ReadyForRendering" && visibleChunks > 0 && position.z == 0f)
            {
                Debug.Log("🎉 TERRAIN RENDERING APPEARS TO BE WORKING! Check your game view.");
            }
            else
            {
                Debug.LogWarning("⚠️  Some issues detected. Check the warnings above.");
            }
        }

        /// <summary>
        /// Manual verification method that can be called from other scripts
        /// </summary>
        public void RunVerification()
        {
            VerifyTerrainRendering();
        }
    }
}