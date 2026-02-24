using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.World.Extensions;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Demo script showing how to use the World Texture Manager with WorldLayer
    /// </summary>
    public class WorldTextureManagerDemo : MonoBehaviour
    {
        [Header("Demo Configuration")]
        [Tooltip("Reference to WorldLayer for testing")]
        [SerializeField] private WorldLayer<CellType> _worldLayer;
        
        [Tooltip("Test region size")]
        [SerializeField] private int _testRegionSize = 10;

        [Header("Debug")]
        [Tooltip("Show debug logs")]
        [SerializeField] private bool _showDebugLogs = true;

        private async void Start()
        {
            if (_worldLayer == null)
            {
                Debug.LogError("WorldLayer reference not set in WorldTextureManagerDemo");
                return;
            }

            await RunTextureManagerDemo();
        }

        private async UniTask RunTextureManagerDemo()
        {
            if (_showDebugLogs)
            {
                Debug.Log("=== World Texture Manager Demo ===");
            }

            // Test 1: Get texture coordinate for a single cell
            var testX = 100;
            var testY = 100;
            var cellType = _worldLayer[testX, testY];

            if (_showDebugLogs)
            {
                Debug.Log($"Testing single cell at ({testX}, {testY}): {cellType}");
            }

            var coordinate = await _worldLayer.GetCellTextureCoordinate(testX, testY);
            
            if (_showDebugLogs)
            {
                Debug.Log($"Texture coordinate: {coordinate}");
                Debug.Log($"UV Rect: {coordinate.UVRect}");
            }

            // Test 2: Preload a region of textures
            if (_showDebugLogs)
            {
                Debug.Log($"Preloading region at ({testX}, {testY}) size {_testRegionSize}x{_testRegionSize}");
            }

            await _worldLayer.PreloadRegionTextures(testX, testY, _testRegionSize, _testRegionSize);

            if (_showDebugLogs)
            {
                Debug.Log("Region preloading completed");
            }

            // Test 3: Get coordinates for entire region
            var regionCoordinates = await _worldLayer.GetRegionTextureCoordinates(testX, testY, _testRegionSize, _testRegionSize);

            if (_showDebugLogs)
            {
                Debug.Log($"Got coordinates for {regionCoordinates.Count} cells in region");
                
                // Show a few sample coordinates
                int count = 0;
                foreach (var kvp in regionCoordinates)
                {
                    if (count < 5)
                    {
                        Debug.Log($"  {kvp.Key}: {kvp.Value}");
                        count++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Test 4: Show atlas information
            var atlases = _worldLayer.GetActiveAtlases();
            
            if (_showDebugLogs)
            {
                Debug.Log($"Active atlases: {atlases.Count}");
                for (int i = 0; i < atlases.Count; i++)
                {
                    Debug.Log($"  Atlas {i}: {atlases[i].Size}x{atlases[i].Size}");
                }
            }

            // Test 5: Show cache statistics
            var cacheStats = _worldLayer.GetTextureCacheStats();
            
            if (_showDebugLogs)
            {
                Debug.Log($"Cache stats: {cacheStats}");
            }

            if (_showDebugLogs)
            {
                Debug.Log("=== Demo completed successfully ===");
            }
        }

        /// <summary>
        /// Manual test method that can be called from inspector
        /// </summary>
        public async void RunManualTest()
        {
            if (_worldLayer == null)
            {
                Debug.LogError("WorldLayer reference not set in WorldTextureManagerDemo");
                return;
            }

            await RunTextureManagerDemo();
        }

        /// <summary>
        /// Clear cache for testing
        /// </summary>
        public void ClearCache()
        {
            _worldLayer.ClearTextureCache();
            Debug.Log("Texture cache cleared");
        }

        /// <summary>
        /// Get current cache statistics
        /// </summary>
        public string GetCacheStats()
        {
            return _worldLayer.GetTextureCacheStats();
        }
    }
}