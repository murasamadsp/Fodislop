using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Networking.Connection;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Comprehensive verification tool for terrain rendering texture application and UV mapping.
    /// Tests material configuration, texture application, and UV coordinate generation.
    /// </summary>
    public class TerrainRenderingVerificationTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable automatic verification testing")]
        [SerializeField] private bool _autoTest = true;
        [Tooltip("Test interval in seconds")]
        [SerializeField] private float _testInterval = 3.0f;
        [Tooltip("Enable detailed logging")]
        [SerializeField] private bool _detailedLogging = true;

        private WorldBackgroundRenderer _renderer;
        private float _lastTestTime = 0f;
        private bool _isInitialized = false;
        private TestResults _lastResults;

        [System.Serializable]
        public class TestResults
        {
            public bool AllTestsPassed;
            public List<TestResult> MaterialTests;
            public List<TestResult> TextureTests;
            public List<TestResult> UVTests;
            public List<TestResult> MeshTests;
            public List<string> Errors;
            public List<string> Warnings;
        }

        [System.Serializable]
        public class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Details;
            public string ErrorMessage;
        }

        void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            _renderer = GetComponent<WorldBackgroundRenderer>();
            _isInitialized = true;

            Debug.Log("TerrainRenderingVerificationTest: Initialized");
        }

        void Update()
        {
            if (!_isInitialized || !_autoTest) return;

            if (Time.time - _lastTestTime >= _testInterval)
            {
                RunVerificationTest();
                _lastTestTime = Time.time;
            }
        }

        /// <summary>
        /// Run comprehensive verification tests
        /// </summary>
        public void RunVerificationTest()
        {
            if (!_isInitialized)
            {
                Debug.LogError("TerrainRenderingVerificationTest: Not initialized");
                return;
            }

            Debug.Log("=== TERRAIN RENDERING VERIFICATION TEST ===");

            var results = new TestResults
            {
                MaterialTests = new List<TestResult>(),
                TextureTests = new List<TestResult>(),
                UVTests = new List<TestResult>(),
                MeshTests = new List<TestResult>(),
                Errors = new List<string>(),
                Warnings = new List<string>()
            };

            // 1. Material Configuration Tests
            TestMaterialConfiguration(results);

            // 2. Texture Application Tests
            TestTextureApplication(results);

            // 3. UV Mapping Tests
            TestUVMapping(results);

            // 4. Mesh Generation Tests
            TestMeshGeneration(results);

            // Calculate overall results
            CalculateTestResults(results);

            // Store and log results
            _lastResults = results;
            LogTestResults(results);

            Debug.Log("=== END VERIFICATION TEST ===");
        }

        private void TestMaterialConfiguration(TestResults results)
        {
            Debug.Log("--- MATERIAL CONFIGURATION TESTS ---");

            // Test 1: Material exists and is configured
            var materialTest = new TestResult { TestName = "Material Configuration" };
            if (_renderer == null)
            {
                materialTest.Passed = false;
                materialTest.ErrorMessage = "WorldBackgroundRenderer not found";
                results.Errors.Add("WorldBackgroundRenderer component missing");
            }
            else if (!_renderer.IsProperlyConfigured())
            {
                materialTest.Passed = false;
                materialTest.ErrorMessage = "Renderer not properly configured";
                results.Warnings.Add("WorldBackgroundRenderer configuration issues");
            }
            else
            {
                materialTest.Passed = true;
                materialTest.Details = "Renderer properly configured";
            }
            results.MaterialTests.Add(materialTest);

            // Test 2: Material shader compatibility
            var shaderTest = new TestResult { TestName = "Shader Compatibility" };
            var material = _renderer.GetComponent<MeshRenderer>()?.material;
            if (material == null)
            {
                shaderTest.Passed = false;
                shaderTest.ErrorMessage = "Material not found on renderer";
                results.Errors.Add("No material found on MeshRenderer");
            }
            else
            {
                var shaderName = material.shader.name;
                if (shaderName.Contains("Universal Render Pipeline") || shaderName.Contains("Unlit"))
                {
                    shaderTest.Passed = true;
                    shaderTest.Details = $"Compatible shader: {shaderName}";
                }
                else
                {
                    shaderTest.Passed = false;
                    shaderTest.ErrorMessage = $"Incompatible shader: {shaderName}";
                    results.Warnings.Add($"Shader may not be URP compatible: {shaderName}");
                }
            }
            results.MaterialTests.Add(shaderTest);

            // Test 3: Material properties
            var propertiesTest = new TestResult { TestName = "Material Properties" };
            if (material != null)
            {
                var hasBaseMap = material.HasProperty("_BaseMap");
                var hasMainTex = material.HasProperty("_MainTex");
                var hasBaseColor = material.HasProperty("_BaseColor");

                if (hasBaseMap || hasMainTex)
                {
                    propertiesTest.Passed = true;
                    propertiesTest.Details = $"Texture properties: BaseMap={hasBaseMap}, MainTex={hasMainTex}, BaseColor={hasBaseColor}";
                }
                else
                {
                    propertiesTest.Passed = false;
                    propertiesTest.ErrorMessage = "No texture properties found";
                    results.Warnings.Add("Material may not support texture application");
                }
            }
            results.MaterialTests.Add(propertiesTest);
        }

        private void TestTextureApplication(TestResults results)
        {
            Debug.Log("--- TEXTURE APPLICATION TESTS ---");

            // Test 1: Texture loading status
            var textureLoadTest = new TestResult { TestName = "Texture Loading" };
            if (_renderer == null)
            {
                textureLoadTest.Passed = false;
                textureLoadTest.ErrorMessage = "Renderer not available";
            }
            else if (_renderer.AreTexturesLoaded())
            {
                textureLoadTest.Passed = true;
                textureLoadTest.Details = "Textures loaded successfully";
            }
            else
            {
                textureLoadTest.Passed = false;
                textureLoadTest.ErrorMessage = "Textures not loaded";
                results.Warnings.Add("Textures may not be loaded yet");
            }
            results.TextureTests.Add(textureLoadTest);

            // Test 2: Atlas application
            var atlasTest = new TestResult { TestName = "Atlas Application" };
            if (_renderer == null)
            {
                atlasTest.Passed = false;
                atlasTest.ErrorMessage = "Renderer not available";
            }
            else if (_renderer.IsAtlasApplied())
            {
                atlasTest.Passed = true;
                atlasTest.Details = "Atlas texture applied successfully";
            }
            else
            {
                atlasTest.Passed = false;
                atlasTest.ErrorMessage = "Atlas not applied";
                results.Warnings.Add("Atlas texture may not be applied to material");
            }
            results.TextureTests.Add(atlasTest);

            // Test 3: Material texture verification
            var materialTextureTest = new TestResult { TestName = "Material Texture" };
            var material = _renderer?.GetComponent<MeshRenderer>()?.material;
            if (material != null)
            {
                var appliedTexture = material.GetTexture("_BaseMap") ?? 
                                   material.GetTexture("_MainTex") ?? 
                                   material.mainTexture;

                if (appliedTexture != null)
                {
                    materialTextureTest.Passed = true;
                    materialTextureTest.Details = $"Texture applied: {appliedTexture.name} ({appliedTexture.width}x{appliedTexture.height})";
                }
                else
                {
                    materialTextureTest.Passed = false;
                    materialTextureTest.ErrorMessage = "No texture found in material";
                    results.Errors.Add("Texture not applied to material - this causes gray terrain");
                }
            }
            results.TextureTests.Add(materialTextureTest);
        }

        private async void TestUVMapping(TestResults results)
        {
            Debug.Log("--- UV MAPPING TESTS ---");

            // Test 1: UV generation capability
            var uvGenerationTest = new TestResult { TestName = "UV Generation" };
            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                uvGenerationTest.Passed = false;
                uvGenerationTest.ErrorMessage = "MapStorage not ready for UV generation";
                results.Warnings.Add("MapStorage not ready - UV coordinates cannot be generated");
            }
            else if (WorldTextureManager.Instance == null)
            {
                uvGenerationTest.Passed = false;
                uvGenerationTest.ErrorMessage = "WorldTextureManager not available";
                results.Warnings.Add("WorldTextureManager not available for UV coordinate generation");
            }
            else
            {
                uvGenerationTest.Passed = true;
                uvGenerationTest.Details = "UV generation systems ready";
            }
            results.UVTests.Add(uvGenerationTest);

            // Test 2: Sample UV coordinate generation
            var sampleUVTest = new TestResult { TestName = "Sample UV Coordinates" };
            if (MapStorage.Instance != null && MapStorage.Instance.IsReady && WorldTextureManager.Instance != null)
            {
                try
                {
                    // Test with a sample cell type
                    var sampleCellType = CellType.Road;
                    var sampleCoord = await WorldTextureManager.Instance.GetCellTextureCoordinate(sampleCellType, 0, 0);
                    
                    if (sampleCoord != AtlasCoordinate.Empty)
                    {
                        sampleUVTest.Passed = true;
                        sampleUVTest.Details = $"Sample UV: U1={sampleCoord.U1:F3}, V1={sampleCoord.V1:F3}, U2={sampleCoord.U2:F3}, V2={sampleCoord.V2:F3}";
                    }
                    else
                    {
                        sampleUVTest.Passed = false;
                        sampleUVTest.ErrorMessage = "No UV coordinates generated for sample cell";
                        results.Warnings.Add("UV coordinates may not be generated correctly");
                    }
                }
                catch (System.Exception ex)
                {
                    sampleUVTest.Passed = false;
                    sampleUVTest.ErrorMessage = $"Exception during UV generation: {ex.Message}";
                    results.Errors.Add($"UV generation failed: {ex.Message}");
                }
            }
            results.UVTests.Add(sampleUVTest);
        }

        private void TestMeshGeneration(TestResults results)
        {
            Debug.Log("--- MESH GENERATION TESTS ---");

            // Test 1: Mesh component
            var meshTest = new TestResult { TestName = "Mesh Component" };
            var meshFilter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            
            if (meshFilter == null || meshRenderer == null)
            {
                meshTest.Passed = false;
                meshTest.ErrorMessage = "Missing MeshFilter or MeshRenderer";
                results.Errors.Add("Mesh components missing - terrain cannot render");
            }
            else
            {
                meshTest.Passed = true;
                meshTest.Details = "Mesh components present";
            }
            results.MeshTests.Add(meshTest);

            // Test 2: Mesh data
            var meshDataTest = new TestResult { TestName = "Mesh Data" };
            if (meshFilter != null && meshFilter.mesh != null)
            {
                var mesh = meshFilter.mesh;
                var vertexCount = mesh.vertexCount;
                var triangleCount = mesh.triangles.Length / 3;
                var uvCount = mesh.uv.Length;

                if (vertexCount > 0 && triangleCount > 0 && uvCount > 0)
                {
                    meshDataTest.Passed = true;
                    meshDataTest.Details = $"Vertices: {vertexCount}, Triangles: {triangleCount}, UVs: {uvCount}";
                }
                else
                {
                    meshDataTest.Passed = false;
                    meshDataTest.ErrorMessage = $"Empty mesh data: V={vertexCount}, T={triangleCount}, UV={uvCount}";
                    results.Warnings.Add("Mesh may be empty - check chunk generation");
                }
            }
            results.MeshTests.Add(meshDataTest);

            // Test 3: Visible chunks
            var chunkTest = new TestResult { TestName = "Visible Chunks" };
            if (_renderer != null)
            {
                var visibleChunks = _renderer.GetVisibleChunkCount();
                if (visibleChunks > 0)
                {
                    chunkTest.Passed = true;
                    chunkTest.Details = $"Visible chunks: {visibleChunks}";
                }
                else
                {
                    chunkTest.Passed = false;
                    chunkTest.ErrorMessage = "No visible chunks";
                    results.Warnings.Add("No chunks visible - check camera position and render distance");
                }
            }
            results.MeshTests.Add(chunkTest);
        }

        private void CalculateTestResults(TestResults results)
        {
            var allTests = new List<TestResult>();
            allTests.AddRange(results.MaterialTests);
            allTests.AddRange(results.TextureTests);
            allTests.AddRange(results.UVTests);
            allTests.AddRange(results.MeshTests);

            var failedTests = allTests.FindAll(t => !t.Passed);
            var criticalErrors = results.Errors.FindAll(e => e.Contains("gray terrain") || e.Contains("cannot render"));

            results.AllTestsPassed = failedTests.Count == 0 && criticalErrors.Count == 0;
        }

        private void LogTestResults(TestResults results)
        {
            Debug.Log($"Verification Result: {(results.AllTestsPassed ? "PASSED" : "FAILED")}");

            // Log material tests
            Debug.Log("MATERIAL TESTS:");
            foreach (var test in results.MaterialTests)
            {
                var status = test.Passed ? "✓" : "✗";
                Debug.Log($"  {status} {test.TestName}: {test.Details ?? test.ErrorMessage}");
            }

            // Log texture tests
            Debug.Log("TEXTURE TESTS:");
            foreach (var test in results.TextureTests)
            {
                var status = test.Passed ? "✓" : "✗";
                Debug.Log($"  {status} {test.TestName}: {test.Details ?? test.ErrorMessage}");
            }

            // Log UV tests
            Debug.Log("UV TESTS:");
            foreach (var test in results.UVTests)
            {
                var status = test.Passed ? "✓" : "✗";
                Debug.Log($"  {status} {test.TestName}: {test.Details ?? test.ErrorMessage}");
            }

            // Log mesh tests
            Debug.Log("MESH TESTS:");
            foreach (var test in results.MeshTests)
            {
                var status = test.Passed ? "✓" : "✗";
                Debug.Log($"  {status} {test.TestName}: {test.Details ?? test.ErrorMessage}");
            }

            // Log errors and warnings
            if (results.Errors.Count > 0)
            {
                Debug.LogError("CRITICAL ERRORS:");
                foreach (var error in results.Errors)
                {
                    Debug.LogError($"  • {error}");
                }
            }

            if (results.Warnings.Count > 0)
            {
                Debug.LogWarning("WARNINGS:");
                foreach (var warning in results.Warnings)
                {
                    Debug.LogWarning($"  • {warning}");
                }
            }

            // Provide specific guidance for gray terrain
            var grayTerrainError = results.Errors.Find(e => e.Contains("gray terrain"));
            if (grayTerrainError != null)
            {
                Debug.LogError("GRAY TERRAIN DIAGNOSIS:");
                Debug.LogError("  The terrain appears gray because textures are not being applied to the material.");
                Debug.LogError("  Common causes and solutions:");
                Debug.LogError("  1. Material shader not compatible with URP - use 'Universal Render Pipeline/Unlit'");
                Debug.LogError("  2. Texture property not set correctly - ensure _BaseMap or _MainTex is used");
                Debug.LogError("  3. Atlas texture not loaded - check WorldTextureManager");
                Debug.LogError("  4. UV coordinates not generated - check WorldTextureManager.GetCellTextureCoordinate");
            }
        }

        /// <summary>
        /// Get the last test results
        /// </summary>
        public TestResults GetLastResults() => _lastResults;

        /// <summary>
        /// Get a summary of the verification status
        /// </summary>
        public string GetVerificationSummary()
        {
            if (_lastResults == null)
            {
                return "No verification results available";
            }

            var totalTests = _lastResults.MaterialTests.Count + _lastResults.TextureTests.Count + 
                           _lastResults.UVTests.Count + _lastResults.MeshTests.Count;
            var passedTests = totalTests - _lastResults.Errors.Count - _lastResults.Warnings.Count;

            return $"Status: {(_lastResults.AllTestsPassed ? "PASSED" : "FAILED")} | " +
                   $"Tests: {passedTests}/{totalTests} passed | " +
                   $"Errors: {_lastResults.Errors.Count} | " +
                   $"Warnings: {_lastResults.Warnings.Count}";
        }

        /// <summary>
        /// Force a verification test and return results
        /// </summary>
        public TestResults ForceVerification()
        {
            RunVerificationTest();
            return _lastResults;
        }

        /// <summary>
        /// Get troubleshooting information for gray terrain
        /// </summary>
        public string GetGrayTerrainTroubleshooting()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== GRAY TERRAIN TROUBLESHOOTING ===");

            if (_lastResults != null)
            {
                var textureErrors = _lastResults.TextureTests.FindAll(t => !t.Passed);
                var materialErrors = _lastResults.MaterialTests.FindAll(t => !t.Passed);
                var uvErrors = _lastResults.UVTests.FindAll(t => !t.Passed);

                if (textureErrors.Count > 0)
                {
                    info.AppendLine("TEXTURE ISSUES:");
                    foreach (var error in textureErrors)
                    {
                        info.AppendLine($"  • {error.TestName}: {error.ErrorMessage}");
                    }
                }

                if (materialErrors.Count > 0)
                {
                    info.AppendLine("MATERIAL ISSUES:");
                    foreach (var error in materialErrors)
                    {
                        info.AppendLine($"  • {error.TestName}: {error.ErrorMessage}");
                    }
                }

                if (uvErrors.Count > 0)
                {
                    info.AppendLine("UV MAPPING ISSUES:");
                    foreach (var error in uvErrors)
                    {
                        info.AppendLine($"  • {error.TestName}: {error.ErrorMessage}");
                    }
                }
            }

            info.AppendLine("COMMON SOLUTIONS:");
            info.AppendLine("1. Check material shader is 'Universal Render Pipeline/Unlit'");
            info.AppendLine("2. Verify texture is applied to _BaseMap property");
            info.AppendLine("3. Ensure WorldTextureManager is loading textures");
            info.AppendLine("4. Check UV coordinates are being generated correctly");
            info.AppendLine("5. Verify MapStorage has world data");

            info.AppendLine("=== END TROUBLESHOOTING ===");
            return info.ToString();
        }

        private void OnDestroy()
        {
            Debug.Log("TerrainRenderingVerificationTest: Destroyed");
        }
    }
}