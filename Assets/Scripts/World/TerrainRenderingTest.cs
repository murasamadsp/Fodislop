using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Comprehensive test script for terrain rendering system.
    /// This script provides automated testing and debugging tools for the terrain rendering pipeline.
    /// </summary>
    [RequireComponent(typeof(WorldBackgroundRenderer))]
    public class TerrainRenderingTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable automatic testing on start")]
        [SerializeField] private bool _autoTestOnStart = true;
        [Tooltip("Test interval in seconds")]
        [SerializeField] private float _testInterval = 3f;
        [Tooltip("Enable detailed logging")]
        [SerializeField] private bool _enableDetailedLogging = true;

        private WorldBackgroundRenderer _renderer;
        private float _lastTestTime = 0f;
        private bool _isTesting = false;
        private int _testCount = 0;

        void Start()
        {
            _renderer = GetComponent<WorldBackgroundRenderer>();
            
            if (_autoTestOnStart)
            {
                StartCoroutine(RunComprehensiveTest());
            }
        }

        void Update()
        {
            // Run tests periodically if auto-testing is enabled
            if (_autoTestOnStart && Time.time - _lastTestTime >= _testInterval)
            {
                if (!_isTesting)
                {
                    StartCoroutine(RunComprehensiveTest());
                }
            }
        }

        /// <summary>
        /// Run a comprehensive test of the terrain rendering system
        /// </summary>
        public IEnumerator RunComprehensiveTest()
        {
            _isTesting = true;
            _lastTestTime = Time.time;
            _testCount++;

            Debug.Log($"=== Terrain Rendering Test #{_testCount} Started ===");

            // Test 1: System Status Check
            Debug.Log("Test 1: System Status Check");
            yield return TestSystemStatus();

            // Test 2: MapStorage Validation
            Debug.Log("Test 2: MapStorage Validation");
            yield return TestMapStorageValidation();

            // Test 3: WorldBackgroundRenderer State
            Debug.Log("Test 3: WorldBackgroundRenderer State");
            yield return TestRendererState();

            // Test 4: World Data Access
            Debug.Log("Test 4: World Data Access");
            yield return TestWorldDataAccess();

            // Test 5: Mesh Generation
            Debug.Log("Test 5: Mesh Generation");
            yield return TestMeshGeneration();

            // Test 6: Texture Application
            Debug.Log("Test 6: Texture Application");
            yield return TestTextureApplication();

            // Test 7: Force Recovery (if needed)
            Debug.Log("Test 7: Force Recovery");
            yield return TestForceRecovery();

            Debug.Log($"=== Terrain Rendering Test #{_testCount} Completed ===");
            _isTesting = false;
        }

        private IEnumerator TestSystemStatus()
        {
            Debug.Log("  Checking system components...");
            
            // Check MapManager
            if (MapManager.Instance != null)
            {
                Debug.Log($"  ✓ MapManager: Available (World: {MapManager.Instance.WorldDisplayName})");
            }
            else
            {
                Debug.LogError("  ✗ MapManager: Not available");
            }

            // Check MapStorage
            if (MapStorage.Instance != null)
            {
                Debug.Log($"  ✓ MapStorage: Available (Ready: {MapStorage.Instance.IsReady})");
                if (MapStorage.Instance.IsReady)
                {
                    Debug.Log($"  ✓ MapStorage: World '{MapStorage.Instance.GetWorldCodeName()}' initialized");
                }
            }
            else
            {
                Debug.LogError("  ✗ MapStorage: Not available");
            }

            // Check WorldBackgroundRenderer
            if (_renderer != null)
            {
                Debug.Log($"  ✓ WorldBackgroundRenderer: Available");
                Debug.Log($"  ✓ Renderer State: {_renderer.GetRendererState()}");
            }
            else
            {
                Debug.LogError("  ✗ WorldBackgroundRenderer: Not available");
            }

            yield return null;
        }

        private IEnumerator TestMapStorageValidation()
        {
            Debug.Log("  Validating MapStorage...");
            
            if (MapStorage.Instance == null)
            {
                Debug.LogError("  ✗ MapStorage not available for validation");
                yield break;
            }

            if (!MapStorage.Instance.IsReady)
            {
                Debug.LogWarning("  ⚠ MapStorage not ready - this may be expected during initialization");
                yield break;
            }

            // Test cell access with bounds checking
            try
            {
                var testCell1 = MapStorage.Instance.GetCell(0, 0);
                var testCell2 = MapStorage.Instance.GetCell(10, 10);
                var testCell3 = MapStorage.Instance.GetCell(100, 100);
                
                Debug.Log($"  ✓ Cell (0,0): {testCell1}");
                Debug.Log($"  ✓ Cell (10,10): {testCell2}");
                Debug.Log($"  ✓ Cell (100,100): {testCell3}");
                
                // Check if we have valid world data
                bool hasValidData = testCell1 != CellType.Unloaded && testCell1 != CellType.Pregener;
                if (hasValidData)
                {
                    Debug.Log("  ✓ World data appears to be loaded and accessible");
                }
                else
                {
                    Debug.LogWarning("  ⚠ World data may not be fully loaded yet");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"  ✗ Error accessing world data: {ex.Message}");
            }

            yield return null;
        }

        private IEnumerator TestRendererState()
        {
            Debug.Log("  Checking renderer state...");
            
            if (_renderer == null)
            {
                Debug.LogError("  ✗ Renderer not available");
                yield break;
            }

            // Check renderer configuration
            bool isProperlyConfigured = _renderer.IsProperlyConfigured();
            Debug.Log($"  ✓ Renderer properly configured: {isProperlyConfigured}");
            
            int visibleChunks = _renderer.GetVisibleChunkCount();
            Debug.Log($"  ✓ Visible chunks: {visibleChunks}");
            
            bool texturesLoaded = _renderer.AreTexturesLoaded();
            Debug.Log($"  ✓ Textures loaded: {texturesLoaded}");
            
            bool atlasApplied = _renderer.IsAtlasApplied();
            Debug.Log($"  ✓ Atlas applied: {atlasApplied}");

            // Check mesh state
            var meshFilter = _renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                var mesh = meshFilter.mesh;
                Debug.Log($"  ✓ Mesh vertices: {mesh.vertexCount}");
                Debug.Log($"  ✓ Mesh triangles: {mesh.triangles.Length}");
            }
            else
            {
                Debug.LogWarning("  ⚠ Mesh not yet generated");
            }

            yield return null;
        }

        private IEnumerator TestWorldDataAccess()
        {
            Debug.Log("  Testing world data access...");
            
            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                Debug.LogWarning("  ⚠ Cannot test world data access - MapStorage not ready");
                yield break;
            }

            // Test boundary conditions
            try
            {
                // Test edge cases
                var edgeCell1 = MapStorage.Instance.GetCell(-1, -1); // Should return Unloaded
                var edgeCell2 = MapStorage.Instance.GetCell(999999, 999999); // Should return Unloaded
                
                Debug.Log($"  ✓ Edge cell (-1,-1): {edgeCell1}");
                Debug.Log($"  ✓ Edge cell (999999,999999): {edgeCell2}");
                
                // Test normal access
                var normalCell = MapStorage.Instance.GetCell(50, 50);
                Debug.Log($"  ✓ Normal cell (50,50): {normalCell}");
                
                // Test setting a cell (if world is modifiable)
                MapStorage.Instance.SetCell(50, 50, CellType.Unloaded);
                var afterSet = MapStorage.Instance.GetCell(50, 50);
                Debug.Log($"  ✓ After set (50,50): {afterSet}");
                
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"  ✗ Error in world data access test: {ex.Message}");
            }

            yield return null;
        }

        private IEnumerator TestMeshGeneration()
        {
            Debug.Log("  Testing mesh generation...");
            
            if (_renderer == null)
            {
                Debug.LogError("  ✗ Renderer not available for mesh test");
                yield break;
            }

            // Force mesh update
            _renderer.ForceInitialization();
            yield return new WaitForSeconds(0.5f); // Wait for mesh generation

            var meshFilter = _renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                var mesh = meshFilter.mesh;
                int vertexCount = mesh.vertexCount;
                int triangleCount = mesh.triangles.Length;
                
                Debug.Log($"  ✓ Mesh generated: {vertexCount} vertices, {triangleCount} triangles");
                
                if (vertexCount > 0 && triangleCount > 0)
                {
                    Debug.Log("  ✓ Mesh appears to be properly generated");
                }
                else
                {
                    Debug.LogWarning("  ⚠ Mesh is empty - no vertices or triangles");
                }
            }
            else
            {
                Debug.LogWarning("  ⚠ Mesh not generated yet");
            }

            yield return null;
        }

        private IEnumerator TestTextureApplication()
        {
            Debug.Log("  Testing texture application...");
            
            if (_renderer == null)
            {
                Debug.LogError("  ✗ Renderer not available for texture test");
                yield break;
            }

            var meshRenderer = _renderer.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.material != null)
            {
                var material = meshRenderer.material;
                var mainTexture = material.mainTexture;
                
                Debug.Log($"  ✓ Material: {material.name}");
                Debug.Log($"  ✓ Main texture: {(mainTexture != null ? mainTexture.name : "null")}");
                
                if (mainTexture != null)
                {
                    Debug.Log($"  ✓ Texture size: {mainTexture.width}x{mainTexture.height}");
                    Debug.Log("  ✓ Texture successfully applied to material");
                }
                else
                {
                    Debug.LogWarning("  ⚠ No texture applied to material");
                }
            }
            else
            {
                Debug.LogWarning("  ⚠ Renderer material not available");
            }

            yield return null;
        }

        private IEnumerator TestForceRecovery()
        {
            Debug.Log("  Testing force recovery mechanisms...");
            
            bool recoveryNeeded = false;
            string recoveryReason = "";

            // Check if recovery is needed
            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                recoveryNeeded = true;
                recoveryReason = "MapStorage not ready";
            }
            else if (_renderer != null && !_renderer.IsProperlyConfigured())
            {
                recoveryNeeded = true;
                recoveryReason = "Renderer not properly configured";
            }
            else if (_renderer != null && _renderer.GetVisibleChunkCount() == 0)
            {
                recoveryNeeded = true;
                recoveryReason = "No visible chunks";
            }

            if (recoveryNeeded)
            {
                Debug.LogWarning($"  ⚠ Recovery needed: {recoveryReason}");
                
                // Attempt recovery
                if (MapStorage.Instance != null && !MapStorage.Instance.IsReady)
                {
                    Debug.Log("  Attempting MapStorage recovery...");
                    // MapStorage should auto-recover through its InitWorld method
                }

                if (_renderer != null)
                {
                    Debug.Log("  Attempting renderer recovery...");
                    _renderer.ForceReinitialize();
                    yield return new WaitForSeconds(1.0f);
                    
                    // Check if recovery was successful
                    if (_renderer.IsProperlyConfigured() && _renderer.GetVisibleChunkCount() > 0)
                    {
                        Debug.Log("  ✓ Recovery successful");
                    }
                    else
                    {
                        Debug.LogWarning("  ⚠ Recovery may have failed - check logs for details");
                    }
                }
            }
            else
            {
                Debug.Log("  ✓ No recovery needed - system appears healthy");
            }

            yield return null;
        }

        /// <summary>
        /// Get the current renderer state for debugging
        /// </summary>
        public string GetRendererState()
        {
            if (_renderer == null) return "Null";
            
            // This would need to be implemented in WorldBackgroundRenderer
            // For now, return a basic status
            return "Active";
        }

        /// <summary>
        /// Run a quick diagnostic check
        /// </summary>
        public void QuickDiagnostic()
        {
            Debug.Log("=== Quick Terrain Diagnostic ===");
            Debug.Log($"MapStorage Ready: {MapStorage.Instance?.IsReady ?? false}");
            Debug.Log($"MapManager Available: {MapManager.Instance != null}");
            Debug.Log($"Renderer Configured: {_renderer?.IsProperlyConfigured() ?? false}");
            Debug.Log($"Visible Chunks: {_renderer?.GetVisibleChunkCount() ?? 0}");
            Debug.Log("================================");
        }

        /// <summary>
        /// Force a complete system reset and re-initialization
        /// </summary>
        public void ForceSystemReset()
        {
            Debug.Log("=== Forcing System Reset ===");
            
            // Reset MapStorage
            if (MapStorage.Instance != null)
            {
                MapStorage.Instance.Dispose();
                Debug.Log("MapStorage disposed");
            }

            // Reset renderer
            if (_renderer != null)
            {
                _renderer.ForceReinitialize();
                Debug.Log("Renderer reinitialized");
            }

            // Wait and test again
            StartCoroutine(DelayedDiagnostic());
        }

        private IEnumerator DelayedDiagnostic()
        {
            yield return new WaitForSeconds(2.0f);
            QuickDiagnostic();
        }

        private void OnValidate()
        {
            // Ensure test interval is reasonable
            if (_testInterval < 1f) _testInterval = 1f;
            if (_testInterval > 60f) _testInterval = 60f;
        }
    }
}