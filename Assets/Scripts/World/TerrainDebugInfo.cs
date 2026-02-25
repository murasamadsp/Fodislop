using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Networking.Connection;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Debug component to display terrain rendering information in the scene
    /// </summary>
    [ExecuteAlways]
    public class TerrainDebugInfo : MonoBehaviour
    {
        [Header("Debug Settings")]
        [Tooltip("Enable debug info display")]
        [SerializeField] private bool _showDebugInfo = true;
        [Tooltip("Update interval in seconds")]
        [SerializeField] private float _updateInterval = 1.0f;

        private float _lastUpdateTime = 0f;
        private string _debugText = "";

        void Update()
        {
            if (!_showDebugInfo) return;
            
            if (Time.time - _lastUpdateTime >= _updateInterval)
            {
                UpdateDebugInfo();
                _lastUpdateTime = Time.time;
            }
        }

        private void UpdateDebugInfo()
        {
            var info = new List<string>();
            info.Add("=== Terrain Debug Info ===");

            // Check MapStorage
            if (MapStorage.Instance?.cellLayer != null)
            {
                info.Add($"✓ MapStorage: Active");
                info.Add($"  - World size: {MapStorage.Instance.cellLayer.WidthChunks * 32} x {MapStorage.Instance.cellLayer.HeightChunks * 32}");
                info.Add($"  - Cell layer: {MapStorage.Instance.cellLayer.WidthChunks} x {MapStorage.Instance.cellLayer.HeightChunks}");
            }
            else
            {
                info.Add($"✗ MapStorage: Not initialized");
            }

            // Check WorldTextureManager
            if (WorldTextureManager.Instance != null)
            {
                info.Add($"✓ WorldTextureManager: Active");
                var atlases = WorldTextureManager.Instance.GetAllAtlases();
                info.Add($"  - Atlases: {atlases.Count}");
                if (atlases.Count > 0)
                {
                    info.Add($"  - Atlas size: {atlases[0].Size}x{atlases[0].Size}");
                    //info.Add($"  - Atlas textures: {atlases[0].GetAtlasTexture().Result?.width}x{atlases[0].GetAtlasTexture().Result?.height}");
                }
            }
            else
            {
                info.Add($"✗ WorldTextureManager: Not found");
            }

            // Check WorldBackgroundRenderer
            var renderer = FindObjectOfType<WorldBackgroundRenderer>();
            if (renderer != null)
            {
                info.Add($"✓ WorldBackgroundRenderer: Found");
                
                // Get renderer state using reflection since state is private
                var stateField = typeof(WorldBackgroundRenderer).GetField("_currentState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stateField != null)
                {
                    var state = stateField.GetValue(renderer);
                    info.Add($"  - State: {state}");
                }
                
                info.Add($"  - Chunks: {renderer.GetVisibleChunkCount()}");
                info.Add($"  - Textures loaded: {renderer.AreTexturesLoaded()}");
                info.Add($"  - Atlas applied: {renderer.IsAtlasApplied()}");
                
                // Check material
                var meshRenderer = renderer.GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.material != null)
                {
                    info.Add($"  - Material: {meshRenderer.material.name}");
                    info.Add($"  - Material texture: {(meshRenderer.material.mainTexture != null ? "Assigned" : "Missing")}");
                }
                else
                {
                    info.Add($"  - Material: Missing");
                }
                
                // Check mesh
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    info.Add($"  - Mesh vertices: {meshFilter.mesh.vertexCount}");
                    info.Add($"  - Mesh triangles: {meshFilter.mesh.triangles.Length / 3}");
                }
                else
                {
                    info.Add($"  - Mesh: Missing");
                }
            }
            else
            {
                info.Add($"✗ WorldBackgroundRenderer: Not found");
            }

            // Check ConnectionManager
            if (ConnectionManager.Instance != null)
            {
                info.Add($"✓ ConnectionManager: Active");
                info.Add($"  - Status: {ConnectionManager.Instance.Connection?.ConnectionStatus}");
            }
            else
            {
                info.Add($"✗ ConnectionManager: Not found");
            }

            // Check Scene Setup
            var sceneSetup = FindObjectOfType<SceneSetup>();
            if (sceneSetup != null)
            {
                info.Add($"✓ SceneSetup: Found");
            }
            else
            {
                info.Add($"✗ SceneSetup: Not found");
            }

            // Check WorldBackgroundSetup
            var backgroundSetup = FindObjectOfType<WorldBackgroundSetup>();
            if (backgroundSetup != null)
            {
                info.Add($"✓ WorldBackgroundSetup: Found");
            }
            else
            {
                info.Add($"✗ WorldBackgroundSetup: Not found");
            }

            _debugText = string.Join("\n", info);
        }

        void OnGUI()
        {
            if (!_showDebugInfo) return;

            GUI.Label(new Rect(10, 10, 400, 300), _debugText);
        }
    }
}