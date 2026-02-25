using System;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Helper component for testing terrain rendering functionality
    /// </summary>
    [ExecuteAlways]
    public class TerrainTestHelper : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable test mode")]
        [SerializeField] private bool _enableTestMode = false;
        [Tooltip("Test world size")]
        [SerializeField] private int _testWorldWidth = 64;
        [Tooltip("Test world height")]
        [SerializeField] private int _testWorldHeight = 64;
        [Tooltip("Test cell type to use")]
        [SerializeField] private CellType _testCellType = CellType.Empty;

        private WorldBackgroundRenderer _backgroundRenderer;
        private WorldTextureManager _textureManager;
        private MapManager _mapManager;
        private MapStorage _mapStorage;

        void Update()
        {
            if (!_enableTestMode) return;

            // Test initialization status
            TestInitializationStatus();
        }

        private void TestInitializationStatus()
        {
            _backgroundRenderer = FindObjectOfType<WorldBackgroundRenderer>();
            _textureManager = FindObjectOfType<WorldTextureManager>();
            _mapManager = FindObjectOfType<MapManager>();
            _mapStorage = MapStorage.Instance; // MapStorage is a singleton, not a MonoBehaviour

            Debug.Log("=== Terrain Test Status ===");
            
            // Check components
            Debug.Log($"Background Renderer: {(_backgroundRenderer != null ? "Found" : "Missing")}");
            Debug.Log($"Texture Manager: {(_textureManager != null ? "Found" : "Missing")}");
            Debug.Log($"Map Manager: {(_mapManager != null ? "Found" : "Missing")}");
            Debug.Log($"Map Storage: {(_mapStorage != null ? "Found" : "Missing")}");

            // Check renderer configuration
            if (_backgroundRenderer != null)
            {
                Debug.Log($"Renderer Configured: {_backgroundRenderer.IsProperlyConfigured()}");
                Debug.Log($"Visible Chunks: {_backgroundRenderer.GetVisibleChunkCount()}");
                Debug.Log($"Textures Loaded: {_backgroundRenderer.AreTexturesLoaded()}");
                Debug.Log($"Atlas Applied: {_backgroundRenderer.IsAtlasApplied()}");
            }

            // Check world data
            if (_mapStorage != null && _mapStorage.cellLayer != null)
            {
                Debug.Log($"World Layer Active: True");
                Debug.Log($"World Size: {_mapStorage.cellLayer.WidthChunks * 32} x {_mapStorage.cellLayer.HeightChunks * 32}");
            }
            else
            {
                Debug.Log($"World Layer Active: False");
            }

            // Check atlases
            if (_textureManager != null)
            {
                var atlases = _textureManager.GetAllAtlases();
                Debug.Log($"Atlases: {atlases.Count}");
                if (atlases.Count > 0)
                {
                    Debug.Log($"Atlas Size: {atlases[0].Size}x{atlases[0].Size}");
                }
            }
        }

        /// <summary>
        /// Test method to force world initialization with test data
        /// </summary>
        public void TestInitializeWorld()
        {
            if (_mapManager == null)
            {
                Debug.LogError("MapManager not found");
                return;
            }

            if (_mapStorage == null)
            {
                Debug.LogError("MapStorage not found");
                return;
            }

            // Create test world init packet
            var testPacket = new WorldInitPacket
            {
                CodeName = "test_world",
                DisplayName = "Test World",
                Width = (ushort)_testWorldWidth,
                Height = (ushort)_testWorldHeight,
                Cells = new CellConfigurationPacket[256] // Default cell configurations
            };

            // Initialize world
            _mapManager.LoadWorldInit(testPacket);
            Debug.Log($"Test world initialized: {_testWorldWidth}x{_testWorldHeight}");
        }

        /// <summary>
        /// Test method to force renderer reinitialization
        /// </summary>
        public void TestReinitializeRenderer()
        {
            if (_backgroundRenderer != null)
            {
                _backgroundRenderer.ForceReinitialize();
                Debug.Log("Renderer reinitialized");
            }
            else
            {
                Debug.LogError("Background renderer not found");
            }
        }

        /// <summary>
        /// Test method to generate test terrain data
        /// </summary>
        public void TestGenerateTerrain()
        {
            if (_mapStorage == null || _mapStorage.cellLayer == null)
            {
                Debug.LogError("MapStorage not initialized");
                return;
            }

            // Fill world with test data
            for (int y = 0; y < _testWorldHeight; y++)
            {
                for (int x = 0; x < _testWorldWidth; x++)
                {
                    _mapStorage.cellLayer[x, y] = _testCellType;
                }
            }

            Debug.Log($"Test terrain generated: {_testWorldWidth}x{_testWorldHeight} with {_testCellType}");
        }

        /// <summary>
        /// Test method to check texture loading
        /// </summary>
        public async void TestTextureLoading()
        {
            if (_textureManager == null)
            {
                Debug.LogError("TextureManager not found");
                return;
            }

            // Try to get texture coordinate for test cell type
            var coord = await _textureManager.GetCellTextureCoordinate(_testCellType, 0, 0);
            Debug.Log($"Texture coordinate for {_testCellType}: {coord}");
        }

        /// <summary>
        /// Test method to verify the complete initialization flow
        /// </summary>
        public void TestCompleteInitialization()
        {
            Debug.Log("=== Testing Complete Initialization Flow ===");
            
            // 1. Initialize world
            TestInitializeWorld();
            
            // 2. Generate terrain data
            TestGenerateTerrain();
            
            // 3. Reinitialize renderer
            TestReinitializeRenderer();
            
            // 4. Check final status
            TestInitializationStatus();
            
            Debug.Log("=== Initialization Test Complete ===");
        }

        private void OnValidate()
        {
            if (_enableTestMode)
            {
                Debug.Log("Terrain Test Mode Enabled");
            }
        }
    }
}