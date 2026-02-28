using System;
using System.Collections.Generic;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Networking;
using Fodinae.Assets.Scripts.Networking.Connection;
using System.Linq;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Comprehensive diagnostic tool for terrain rendering issues.
    /// Provides detailed status checks and troubleshooting guidance.
    /// </summary>
    [RequireComponent(typeof(WorldBackgroundRenderer))]
    public class TerrainRenderingDiagnosticTool : MonoBehaviour
    {
        [Header("Diagnostic Settings")]
        [Tooltip("Enable automatic diagnostic checks")]
        [SerializeField] private bool _autoCheck = true;
        
        [Tooltip("Check interval in seconds")]
        [SerializeField] private float _checkInterval = 5.0f;
        
        [Tooltip("Enable detailed logging")]
        [SerializeField] private bool _detailedLogging = true;
        
        [Tooltip("Enable automatic fixes when possible")]
        [SerializeField] private bool _autoFix = true;

        private WorldBackgroundRenderer _renderer;
        private float _lastCheckTime = 0f;
        private bool _isInitialized = false;
        
        // Diagnostic results
        private DiagnosticResult _lastResult;
        private List<string> _errorHistory = new List<string>();

        [System.Serializable]
        public class DiagnosticResult
        {
            public bool IsHealthy;
            public string OverallStatus;
            public List<DiagnosticCheck> Checks;
            public List<string> CriticalErrors;
            public List<string> Warnings;
            public List<string> Suggestions;
        }

        [System.Serializable]
        public class DiagnosticCheck
        {
            public string Name;
            public bool Passed;
            public string Status;
            public string Details;
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
            
            Debug.Log("TerrainRenderingDiagnosticTool: Initialized");
        }

        void Update()
        {
            if (!_isInitialized || !_autoCheck) return;

            if (Time.time - _lastCheckTime >= _checkInterval)
            {
                RunDiagnosticCheck();
                _lastCheckTime = Time.time;
            }
        }

        /// <summary>
        /// Run a comprehensive diagnostic check
        /// </summary>
        public void RunDiagnosticCheck()
        {
            if (!_isInitialized)
            {
                Debug.LogError("TerrainRenderingDiagnosticTool: Not initialized");
                return;
            }

            Debug.Log("=== TERRAIN RENDERING DIAGNOSTIC CHECK ===");

            var result = new DiagnosticResult
            {
                Checks = new List<DiagnosticCheck>(),
                CriticalErrors = new List<string>(),
                Warnings = new List<string>(),
                Suggestions = new List<string>()
            };

            // 1. Check WorldBackgroundRenderer
            CheckRenderer(result);

            // 2. Check MapStorage
            CheckMapStorage(result);

            // 3. Check MapManager
            CheckMapManager(result);

            // 4. Check Connection/PacketHandler
            CheckConnection(result);

            // 5. Check WorldTextureManager
            CheckTextureManager(result);

            // 6. Check StandaloneWorldInitializer
            CheckStandaloneInitializer(result);

            // Calculate overall status
            CalculateOverallStatus(result);

            // Store result
            _lastResult = result;

            // Log results
            LogDiagnosticResults(result);

            // Apply auto-fixes if enabled
            if (_autoFix)
            {
                ApplyAutoFixes(result);
            }

            Debug.Log("=== END DIAGNOSTIC CHECK ===");
        }

        private void CheckRenderer(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "WorldBackgroundRenderer" };

            if (_renderer == null)
            {
                check.Passed = false;
                check.Status = "MISSING";
                check.Details = "WorldBackgroundRenderer component not found";
                result.CriticalErrors.Add("WorldBackgroundRenderer component is missing");
                result.Suggestions.Add("Add WorldBackgroundRenderer component to this GameObject");
            }
            else if (!_renderer.IsProperlyConfigured())
            {
                check.Passed = false;
                check.Status = "CONFIGURATION_ERROR";
                check.Details = "Renderer not properly configured";
                result.CriticalErrors.Add("WorldBackgroundRenderer is not properly configured");
                result.Suggestions.Add("Check WorldBackgroundRenderer configuration in inspector");
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = $"State: {_renderer.GetRendererState()}, Visible chunks: {_renderer.GetVisibleChunkCount()}";
            }

            result.Checks.Add(check);
        }

        private void CheckMapStorage(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "MapStorage" };

            if (MapStorage.Instance == null)
            {
                check.Passed = false;
                check.Status = "MISSING";
                check.Details = "MapStorage singleton not found";
                result.CriticalErrors.Add("MapStorage singleton is not available");
                result.Suggestions.Add("Ensure MapStorage script is in the scene or properly initialized");
            }
            else if (!MapStorage.Instance.IsReady)
            {
                check.Passed = false;
                check.Status = "NOT_READY";
                check.Details = $"IsReady: {MapStorage.Instance.IsReady}, IsInitialized: {MapStorage.Instance.IsInitialized()}, cellLayer: {(MapStorage.Instance.cellLayer != null ? "not null" : "NULL")}";
                result.CriticalErrors.Add("MapStorage is not ready for terrain rendering");
                result.Suggestions.Add("Check MapStorage initialization - this is critical for terrain rendering");
                
                if (MapStorage.Instance.cellLayer == null)
                {
                    result.CriticalErrors.Add("MapStorage.cellLayer is NULL - WorldLayer creation failed");
                    result.Suggestions.Add("Check file permissions and disk space for WorldLayer creation");
                }
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = $"World: {MapStorage.Instance.GetWorldCodeName()}, Ready: {MapStorage.Instance.IsReady}";
            }

            result.Checks.Add(check);
        }

        private void CheckMapManager(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "MapManager" };

            if (MapManager.Instance == null)
            {
                check.Passed = false;
                check.Status = "MISSING";
                check.Details = "MapManager singleton not found";
                result.CriticalErrors.Add("MapManager singleton is not available");
                result.Suggestions.Add("Ensure MapManager script is in the scene or properly initialized");
            }
            else if (!MapManager.Instance._isWorldInitialized)
            {
                check.Passed = false;
                check.Status = "NOT_INITIALIZED";
                check.Details = "World not initialized";
                result.Warnings.Add("MapManager world not initialized - waiting for WorldInit packet");
                result.Suggestions.Add("Send WorldInit packet or use StandaloneWorldInitializer");
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = $"World: {MapManager.Instance.WorldDisplayName} ({MapManager.Instance.WorldCodeName}), Size: {MapManager.Instance.WorldWidth}x{MapManager.Instance.WorldHeight}";
            }

            result.Checks.Add(check);
        }

        private void CheckConnection(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "Connection/PacketHandler" };

            var connectionManager = ConnectionManager.Instance;
            var packetHandler = FindObjectOfType<PacketHandler>();

            if (connectionManager == null)
            {
                check.Passed = false;
                check.Status = "MISSING";
                check.Details = "ConnectionManager not found";
                result.Warnings.Add("ConnectionManager not available - using standalone mode");
                result.Suggestions.Add("Add ConnectionManager to scene or use StandaloneWorldInitializer");
            }
            else if (connectionManager.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                check.Passed = false;
                check.Status = "NOT_CONNECTED";
                check.Details = "No active connection";
                result.Warnings.Add("No active network connection - using standalone mode");
                result.Suggestions.Add("Establish connection or use StandaloneWorldInitializer");
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = "Connection active";
            }

            if (packetHandler == null)
            {
                check.Details += ", PacketHandler not found";
                result.Warnings.Add("PacketHandler not found - packet processing may not work");
                result.Suggestions.Add("Add PacketHandler to scene for network packet processing");
            }

            result.Checks.Add(check);
        }

        private void CheckTextureManager(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "WorldTextureManager" };

            var textureManager = WorldTextureManager.Instance;

            if (textureManager == null)
            {
                check.Passed = false;
                check.Status = "MISSING";
                check.Details = "WorldTextureManager not found";
                result.Warnings.Add("WorldTextureManager not available");
                result.Suggestions.Add("Ensure WorldTextureManager is in the scene");
            }
            else if (!_renderer.AreTexturesLoaded())
            {
                check.Passed = false;
                check.Status = "TEXTURES_LOADING";
                check.Details = "Textures not yet loaded";
                result.Warnings.Add("Textures still loading");
                result.Suggestions.Add("Wait for texture loading to complete");
            }
            else if (!_renderer.IsAtlasApplied())
            {
                check.Passed = false;
                check.Status = "ATLAS_NOT_APPLIED";
                check.Details = "Atlas texture not applied to material";
                result.Warnings.Add("Atlas texture not applied to renderer material");
                result.Suggestions.Add("Check WorldTextureManager atlas creation and application");
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = "Textures loaded and applied";
            }

            result.Checks.Add(check);
        }

        private void CheckStandaloneInitializer(DiagnosticResult result)
        {
            var check = new DiagnosticCheck { Name = "StandaloneWorldInitializer" };

            var standaloneInit = FindObjectOfType<StandaloneWorldInitializer>();

            if (standaloneInit == null)
            {
                check.Passed = false;
                check.Status = "NOT_FOUND";
                check.Details = "StandaloneWorldInitializer not in scene";
                result.Warnings.Add("No StandaloneWorldInitializer found");
                result.Suggestions.Add("Add StandaloneWorldInitializer for standalone mode");
            }
            else if (!standaloneInit.IsReady())
            {
                check.Passed = false;
                check.Status = "NOT_READY";
                check.Details = "Standalone initializer not ready";
                result.Warnings.Add("StandaloneWorldInitializer not ready");
                result.Suggestions.Add("Check StandaloneWorldInitializer configuration");
            }
            else
            {
                check.Passed = true;
                check.Status = "OK";
                check.Details = "Standalone mode ready";
            }

            result.Checks.Add(check);
        }

        private void CalculateOverallStatus(DiagnosticResult result)
        {
            var criticalCount = result.CriticalErrors.Count;
            var warningCount = result.Warnings.Count;

            if (criticalCount > 0)
            {
                result.IsHealthy = false;
                result.OverallStatus = $"CRITICAL ({criticalCount} errors)";
            }
            else if (warningCount > 0)
            {
                result.IsHealthy = false;
                result.OverallStatus = $"WARNING ({warningCount} warnings)";
            }
            else
            {
                result.IsHealthy = true;
                result.OverallStatus = "HEALTHY";
            }
        }

        private void LogDiagnosticResults(DiagnosticResult result)
        {
            Debug.Log($"Diagnostic Result: {result.OverallStatus}");

            foreach (var check in result.Checks)
            {
                var status = check.Passed ? "✓" : "✗";
                Debug.Log($"{status} {check.Name}: {check.Status} - {check.Details}");
            }

            if (result.CriticalErrors.Count > 0)
            {
                Debug.LogError("CRITICAL ERRORS:");
                foreach (var error in result.CriticalErrors)
                {
                    Debug.LogError($"  • {error}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                Debug.LogWarning("WARNINGS:");
                foreach (var warning in result.Warnings)
                {
                    Debug.LogWarning($"  • {warning}");
                }
            }

            if (result.Suggestions.Count > 0)
            {
                Debug.Log("SUGGESTIONS:");
                foreach (var suggestion in result.Suggestions)
                {
                    Debug.Log($"  • {suggestion}");
                }
            }
        }

        private void ApplyAutoFixes(DiagnosticResult result)
        {
            Debug.Log("=== APPLYING AUTO-FIXES ===");

            foreach (var check in result.Checks)
            {
                if (!check.Passed)
                {
                    ApplyFixForCheck(check);
                }
            }

            Debug.Log("=== AUTO-FIXES COMPLETE ===");
        }

        private void ApplyFixForCheck(DiagnosticCheck check)
        {
            switch (check.Name)
            {
                case "MapStorage":
                    if (check.Status == "NOT_READY")
                    {
                        Debug.Log("Attempting to fix MapStorage...");
                        if (MapManager.Instance != null && MapManager.Instance._isWorldInitialized)
                        {
                            try
                            {
                                MapStorage.Instance.InitWorld(
                                    MapManager.Instance.WorldCodeName,
                                    MapManager.Instance.WorldWidth,
                                    MapManager.Instance.WorldHeight
                                );
                                Debug.Log("MapStorage re-initialization attempted");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"MapStorage fix failed: {ex.Message}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("Cannot fix MapStorage - no world data available");
                        }
                    }
                    break;

                case "MapManager":
                    if (check.Status == "NOT_INITIALIZED")
                    {
                        Debug.Log("Attempting to fix MapManager...");
                        var standaloneInit = FindObjectOfType<StandaloneWorldInitializer>();
                        if (standaloneInit != null)
                        {
                            standaloneInit.ForceStandaloneInitialization();
                            Debug.Log("StandaloneWorldInitializer force initialization triggered");
                        }
                        else
                        {
                            Debug.LogWarning("No StandaloneWorldInitializer available for MapManager fix");
                        }
                    }
                    break;

                case "Connection/PacketHandler":
                    if (check.Status == "NOT_CONNECTED")
                    {
                        Debug.Log("Attempting to fix connection...");
                        var standaloneInit = FindObjectOfType<StandaloneWorldInitializer>();
                        if (standaloneInit != null)
                        {
                            standaloneInit.ForceStandaloneInitialization();
                            Debug.Log("StandaloneWorldInitializer force initialization triggered for connection");
                        }
                    }
                    break;

                case "WorldBackgroundRenderer":
                    if (check.Status == "CONFIGURATION_ERROR")
                    {
                        Debug.Log("Attempting to fix WorldBackgroundRenderer...");
                        _renderer.ForceReinitialize();
                        Debug.Log("WorldBackgroundRenderer reinitialization triggered");
                    }
                    break;
            }
        }

        /// <summary>
        /// Get the last diagnostic result
        /// </summary>
        public DiagnosticResult GetLastResult() => _lastResult;

        /// <summary>
        /// Get a summary of the current system status
        /// </summary>
        public string GetStatusSummary()
        {
            if (_lastResult == null)
            {
                return "No diagnostic results available";
            }

            return $"Status: {_lastResult.OverallStatus} | " +
                   $"Checks: {_lastResult.Checks.Count(c => c.Passed)}/{_lastResult.Checks.Count} passed | " +
                   $"Errors: {_lastResult.CriticalErrors.Count} | " +
                   $"Warnings: {_lastResult.Warnings.Count}";
        }

        /// <summary>
        /// Force a diagnostic check and return the result
        /// </summary>
        public DiagnosticResult ForceCheck()
        {
            RunDiagnosticCheck();
            return _lastResult;
        }

        /// <summary>
        /// Get detailed troubleshooting information
        /// </summary>
        public string GetTroubleshootingInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== TERRAIN RENDERING TROUBLESHOOTING ===");

            if (MapStorage.Instance != null && !MapStorage.Instance.IsReady)
            {
                info.AppendLine("ISSUE: MapStorage not ready");
                info.AppendLine("CAUSE: WorldLayer creation failed or MapStorage not initialized");
                info.AppendLine("SOLUTION:");
                info.AppendLine("  1. Check file permissions for persistent data path");
                info.AppendLine("  2. Ensure sufficient disk space");
                info.AppendLine("  3. Verify world dimensions are valid");
                info.AppendLine("  4. Try MapStorage.Instance.InitWorld('test_world', 64, 64)");
            }

            if (MapManager.Instance != null && !MapManager.Instance._isWorldInitialized)
            {
                info.AppendLine("ISSUE: MapManager not initialized");
                info.AppendLine("CAUSE: No WorldInit packet received or standalone mode not configured");
                info.AppendLine("SOLUTION:");
                info.AppendLine("  1. Send WorldInit packet via network connection");
                info.AppendLine("  2. Use StandaloneWorldInitializer for standalone mode");
                info.AppendLine("  3. Call MapManager.Instance.LoadWorldInit() manually");
            }

            if (_renderer != null && _renderer.GetRendererState() != "ReadyForRendering")
            {
                info.AppendLine("ISSUE: WorldBackgroundRenderer not ready");
                info.AppendLine("CAUSE: Renderer state is not ReadyForRendering");
                info.AppendLine("SOLUTION:");
                info.AppendLine("  1. Check MapStorage is ready");
                info.AppendLine("  2. Verify textures are loaded");
                info.AppendLine("  3. Call _renderer.ForceInitialization()");
            }

            info.AppendLine("=== END TROUBLESHOOTING ===");
            return info.ToString();
        }

        /// <summary>
        /// Clear error history
        /// </summary>
        public void ClearHistory()
        {
            _errorHistory.Clear();
            Debug.Log("TerrainRenderingDiagnosticTool: Error history cleared");
        }

        /// <summary>
        /// Export diagnostic report
        /// </summary>
        public string ExportReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== TERRAIN RENDERING DIAGNOSTIC REPORT ===");
            report.AppendLine($"Generated: {DateTime.Now}");
            report.AppendLine($"Auto Check: {_autoCheck}");
            report.AppendLine($"Check Interval: {_checkInterval}s");
            report.AppendLine($"Detailed Logging: {_detailedLogging}");
            report.AppendLine($"Auto Fix: {_autoFix}");
            report.AppendLine();

            if (_lastResult != null)
            {
                report.AppendLine($"Overall Status: {_lastResult.OverallStatus}");
                report.AppendLine();

                report.AppendLine("CHECKS:");
                foreach (var check in _lastResult.Checks)
                {
                    report.AppendLine($"  {check.Name}: {check.Status}");
                    report.AppendLine($"    Details: {check.Details}");
                    report.AppendLine();
                }

                if (_lastResult.CriticalErrors.Count > 0)
                {
                    report.AppendLine("CRITICAL ERRORS:");
                    foreach (var error in _lastResult.CriticalErrors)
                    {
                        report.AppendLine($"  • {error}");
                    }
                    report.AppendLine();
                }

                if (_lastResult.Warnings.Count > 0)
                {
                    report.AppendLine("WARNINGS:");
                    foreach (var warning in _lastResult.Warnings)
                    {
                        report.AppendLine($"  • {warning}");
                    }
                    report.AppendLine();
                }

                if (_lastResult.Suggestions.Count > 0)
                {
                    report.AppendLine("SUGGESTIONS:");
                    foreach (var suggestion in _lastResult.Suggestions)
                    {
                        report.AppendLine($"  • {suggestion}");
                    }
                    report.AppendLine();
                }
            }
            else
            {
                report.AppendLine("No diagnostic results available");
            }

            report.AppendLine("=== END REPORT ===");
            return report.ToString();
        }

        private void OnDestroy()
        {
            Debug.Log("TerrainRenderingDiagnosticTool: Destroyed");
        }
    }
}