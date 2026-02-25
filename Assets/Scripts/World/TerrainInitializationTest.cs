using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Test script to verify terrain rendering initialization and provide debugging tools.
    /// Add this to a GameObject in your scene to test the terrain rendering system.
    /// </summary>
    [RequireComponent(typeof(WorldBackgroundRenderer))]
    public class TerrainInitializationTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable automatic testing on start")]
        [SerializeField] private bool _autoTestOnStart = true;
        [Tooltip("Test interval in seconds")]
        [SerializeField] private float _testInterval = 5f;

        private WorldBackgroundRenderer _renderer;
        private float _lastTestTime = 0f;
        private bool _isTesting = false;

        void Start()
        {
            _renderer = GetComponent<WorldBackgroundRenderer>();
            
            if (_autoTestOnStart)
            {
                StartCoroutine(DelayedTest());
            }
        }

        void Update()
        {
            // Test every interval if auto-testing is enabled
            if (_autoTestOnStart && Time.time - _lastTestTime >= _testInterval)
            {
                if (!_isTesting)
                {
                    StartCoroutine(RunTestCycle());
                }
            }
        }

        private IEnumerator DelayedTest()
        {
            // Wait a few seconds for initialization to complete
            yield return new WaitForSeconds(3f);
            StartCoroutine(RunTestCycle());
        }

        private IEnumerator RunTestCycle()
        {
            _isTesting = true;
            _lastTestTime = Time.time;

            Debug.Log("=== Terrain Initialization Test Started ===");

            // Test 1: Check MapStorage status
            Debug.Log("Test 1: Checking MapStorage...");
            yield return TestMapStorage();

            // Test 2: Check WorldBackgroundRenderer status
            Debug.Log("Test 2: Checking WorldBackgroundRenderer...");
            yield return TestRenderer();

            // Test 3: Check world data availability
            Debug.Log("Test 3: Checking world data...");
            yield return TestWorldData();

            // Test 4: Force initialization if needed
            Debug.Log("Test 4: Attempting force initialization...");
            yield return TestForceInitialization();

            Debug.Log("=== Terrain Initialization Test Completed ===");
            _isTesting = false;
        }

        private IEnumerator TestMapStorage()
        {
            Debug.Log($"  MapStorage Instance: {(MapStorage.Instance != null ? "Available" : "Null")}");
            if (MapStorage.Instance != null)
            {
                Debug.Log($"  MapStorage Ready: {MapStorage.Instance.IsReady}");
                Debug.Log($"  MapStorage World: {MapStorage.Instance.GetWorldCodeName()}");
                Debug.Log($"  MapStorage Initialized: {MapStorage.Instance.IsInitialized()}");
            }
            yield return null;
        }

        private IEnumerator TestRenderer()
        {
            if (_renderer != null)
            {
                Debug.Log($"  Renderer Initialized: {_renderer.IsProperlyConfigured()}");
                Debug.Log($"  Visible Chunks: {_renderer.GetVisibleChunkCount()}");
                Debug.Log($"  Textures Loaded: {_renderer.AreTexturesLoaded()}");
                Debug.Log($"  Atlas Applied: {_renderer.IsAtlasApplied()}");
            }
            else
            {
                Debug.LogError("  Renderer: Not found!");
            }
            yield return null;
        }

        private IEnumerator TestWorldData()
        {
            if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
            {
                // Try to access a few cells to test data availability
                try
                {
                    var testCell1 = MapStorage.Instance.GetCell(0, 0);
                    var testCell2 = MapStorage.Instance.GetCell(10, 10);
                    var testCell3 = MapStorage.Instance.GetCell(100, 100);
                    
                    Debug.Log($"  Test cell (0,0): {testCell1}");
                    Debug.Log($"  Test cell (10,10): {testCell2}");
                    Debug.Log($"  Test cell (100,100): {testCell3}");
                    
                    if (testCell1 != CellType.Unloaded && testCell1 != CellType.Pregener)
                    {
                        Debug.Log("  World data appears to be loaded and accessible!");
                    }
                    else
                    {
                        Debug.LogWarning("  World data may not be fully loaded yet");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"  Error accessing world data: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("  Cannot test world data - MapStorage not ready");
            }
            yield return null;
        }

        private IEnumerator TestForceInitialization()
        {
            if (_renderer != null)
            {
                // Only force if renderer is not properly configured
                if (!_renderer.IsProperlyConfigured() || _renderer.GetVisibleChunkCount() == 0)
                {
                    Debug.Log("  Attempting force initialization...");
                    _renderer.ForceInitialization();
                    yield return new WaitForSeconds(1f); // Wait for initialization
                    
                    Debug.Log($"  After force init - Visible Chunks: {_renderer.GetVisibleChunkCount()}");
                    Debug.Log($"  After force init - Properly Configured: {_renderer.IsProperlyConfigured()}");
                }
                else
                {
                    Debug.Log("  Renderer appears properly configured, skipping force initialization");
                }
            }
            yield return null;
        }

        /// <summary>
        /// Manual test method that can be called from the Unity Editor
        /// </summary>
        public void RunManualTest()
        {
            if (!_isTesting)
            {
                StartCoroutine(RunTestCycle());
            }
        }

        /// <summary>
        /// Get detailed status information
        /// </summary>
        public void GetDetailedStatus()
        {
            Debug.Log("=== Detailed Terrain System Status ===");
            
            if (MapStorage.Instance != null)
            {
                Debug.Log($"MapStorage: Ready={MapStorage.Instance.IsReady}, World={MapStorage.Instance.GetWorldCodeName()}, Initialized={MapStorage.Instance.IsInitialized()}");
            }
            else
            {
                Debug.Log("MapStorage: Not available");
            }

            if (_renderer != null)
            {
                _renderer.DebugInitializationStatus();
            }
            else
            {
                Debug.Log("Renderer: Not available");
            }

            if (MapManager.Instance != null)
            {
                Debug.Log($"MapManager: Available, World={MapManager.Instance.WorldDisplayName}");
            }
            else
            {
                Debug.Log("MapManager: Not available");
            }

            Debug.Log("=====================================");
        }

        /// <summary>
        /// Force re-initialization of the entire terrain system
        /// </summary>
        public void ForceSystemReinitialize()
        {
            Debug.Log("=== Forcing System Re-initialization ===");
            
            if (MapStorage.Instance != null)
            {
                MapStorage.Instance.Dispose();
                Debug.Log("MapStorage disposed");
            }

            if (_renderer != null)
            {
                _renderer.ForceReinitialize();
                Debug.Log("Renderer reinitialized");
            }

            // Wait a moment then test again
            StartCoroutine(DelayedTest());
        }

        private void OnValidate()
        {
            // Ensure test interval is reasonable
            if (_testInterval < 1f) _testInterval = 1f;
            if (_testInterval > 60f) _testInterval = 60f;
        }
    }
}