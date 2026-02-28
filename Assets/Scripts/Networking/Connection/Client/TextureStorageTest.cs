using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Networking.Connection.Client
{
    /// <summary>
    /// Test script for the TextureStorageManager functionality.
    /// Can be used to verify that the texture storage system is working correctly.
    /// </summary>
    public class TextureStorageTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable debug logging during tests")]
        [SerializeField] private bool _enableDebugLogging = true;
        
        [Tooltip("Test texture filenames to check")]
        [SerializeField] private string[] _testFilenames = 
        {
            "/cells/1.png",
            "/cells/2.png", 
            "/cells/42.png",
            "/cells/255.png"
        };

        private async void Start()
        {
            await TestTextureStorage();
        }

        /// <summary>
        /// Run comprehensive tests of the texture storage system
        /// </summary>
        private async UniTask TestTextureStorage()
        {
            if (_enableDebugLogging)
                Debug.Log("[TextureStorageTest] Starting texture storage tests...");

            var manager = TextureStorageManager.Instance;
            
            if (manager == null)
            {
                Debug.LogError("[TextureStorageTest] TextureStorageManager not found!");
                return;
            }

            // Test 1: Check folder initialization
            var folderPath = manager.GetTextureFolderPath();
            if (_enableDebugLogging)
                Debug.Log($"[TextureStorageTest] Texture folder path: {folderPath}");

            // Test 2: Check cache stats
            var cacheStats = manager.GetCacheStats();
            if (_enableDebugLogging)
                Debug.Log($"[TextureStorageTest] Cache stats: {cacheStats}");

            // Test 3: Test texture loading for each test filename
            var testResults = new List<string>();
            
            foreach (var filename in _testFilenames)
            {
                var startTime = Time.realtimeSinceStartup;
                
                // Test if texture exists
                var hasTexture = manager.HasTexture(filename);
                
                // Test loading texture
                var textureData = await manager.GetTextureData(filename);
                
                var loadTime = Time.realtimeSinceStartup - startTime;
                
                var result = textureData != null ? "SUCCESS" : "FAILED";
                var source = hasTexture ? "FROM_STORAGE" : "FALLBACK_GENERATED";
                
                var testResult = $"[TextureStorageTest] {filename}: {result} ({source}) in {loadTime:F3}s";
                testResults.Add(testResult);
                
                if (_enableDebugLogging)
                    Debug.Log(testResult);
            }

            // Test 4: Test cache hit (should be faster)
            if (_testFilenames.Length > 0)
            {
                var cacheTestFile = _testFilenames[0];
                var startTime = Time.realtimeSinceStartup;
                
                var cachedData = await manager.GetTextureData(cacheTestFile);
                var cacheLoadTime = Time.realtimeSinceStartup - startTime;
                
                var cacheResult = $"[TextureStorageTest] Cache test for {cacheTestFile}: {cacheLoadTime:F3}s";
                testResults.Add(cacheResult);
                
                if (_enableDebugLogging)
                    Debug.Log(cacheResult);
            }

            // Test 5: Final cache stats
            var finalCacheStats = manager.GetCacheStats();
            testResults.Add($"[TextureStorageTest] Final cache stats: {finalCacheStats}");
            
            if (_enableDebugLogging)
                Debug.Log($"[TextureStorageTest] Final cache stats: {finalCacheStats}");

            // Log all results
            Debug.Log("[TextureStorageTest] === TEST RESULTS ===");
            foreach (var result in testResults)
            {
                Debug.Log(result);
            }
            Debug.Log("[TextureStorageTest] === TEST COMPLETE ===");
        }

        /// <summary>
        /// Clear cache for testing purposes
        /// </summary>
        public void ClearCache()
        {
            TextureStorageManager.Instance?.ClearCache();
        }

        /// <summary>
        /// Get current cache statistics
        /// </summary>
        /// <returns>Cache statistics string</returns>
        public string GetCacheStats()
        {
            return TextureStorageManager.Instance?.GetCacheStats() ?? "Manager not found";
        }

        /// <summary>
        /// Test loading a specific texture file
        /// </summary>
        /// <param name="filename">The texture filename to test</param>
        /// <returns>True if loading succeeded</returns>
        public async UniTask<bool> TestTextureLoad(string filename)
        {
            var manager = TextureStorageManager.Instance;
            if (manager == null) return false;

            var hasTexture = manager.HasTexture(filename);
            var textureData = await manager.GetTextureData(filename);
            
            if (_enableDebugLogging)
            {
                Debug.Log($"[TextureStorageTest] TestTextureLoad: {filename}");
                Debug.Log($"  Exists in storage: {hasTexture}");
                Debug.Log($"  Load successful: {textureData != null}");
            }

            return textureData != null;
        }
    }
}