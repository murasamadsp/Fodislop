#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Repeatable Fodinae player builds (menu + headless CLI).
    ///
    /// CLI:
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod Fodinae.Editor.BuildScript.BuildMacOS
    ///   Add -fodinaeDev for a Development build (debugging + profiler).
    ///   Exit code is non-zero on failure (CI-friendly).
    ///
    /// Menu: Build > macOS (Apple Silicon) / Windows 64.
    /// Output goes to Build/&lt;platform&gt;/ (gitignored).
    /// </summary>
    public static class BuildScript
    {
        private const string ProductName = "Fodinae";
        private const string DevArg = "-fodinaeDev";

        private static string[] EnabledScenes =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("Build/macOS (Apple Silicon)")]
        public static void BuildMacOS() =>
            Build(BuildTarget.StandaloneOSX, $"Build/macOS/{ProductName}.app", isApple: true);

        [MenuItem("Build/Windows 64")]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, $"Build/Windows/{ProductName}.exe");

        private static void Build(BuildTarget target, string relativeOutput, bool isApple = false)
        {
            var scenes = EnabledScenes;
            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in EditorBuildSettings — nothing to build.");
                return;
            }

            string output = Path.GetFullPath(relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            if (isApple)
            {
                TrySetAppleSiliconArchitecture();
            }

            try
            {
                var standaloneTarget = UnityEditor.Build.NamedBuildTarget.Standalone;
                PlayerSettings.SetIl2CppCompilerConfiguration(standaloneTarget, Il2CppCompilerConfiguration.Master);
                PlayerSettings.SetIl2CppCodeGeneration(standaloneTarget, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
                PlayerSettings.SetManagedStrippingLevel(standaloneTarget, ManagedStrippingLevel.Minimal);
            }
            catch (Exception ex)
            {
                Log($"Optimization settings notice: {ex.Message}");
            }

            bool development = Environment.GetCommandLineArgs().Contains(DevArg);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Log($"Building {target} -> {output} (development={development}, scenes={scenes.Length})");
            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            Log($"Result={summary.result} size={summary.totalSize}B " +
                $"time={summary.totalTime} warnings={summary.totalWarnings} errors={summary.totalErrors}");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build failed: {summary.result} ({summary.totalErrors} errors).");
                return;
            }

            CopyRuntimeTextures(target, output);
            Log($"Build succeeded: {output}");
        }

        private static void CopyRuntimeTextures(BuildTarget target, string playerPath)
        {
            string source = Path.GetFullPath(Path.Combine("Assets", "Textures"));
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    $"Required runtime texture directory does not exist: {source}");
            }

            string playerDirectory = Path.GetDirectoryName(playerPath) ??
                throw new InvalidOperationException(
                    $"Player output has no parent directory: {playerPath}");

            List<string> destinations = new();
            if (target == BuildTarget.StandaloneOSX)
            {
                destinations.Add(Path.Combine(playerPath, "Contents", "Resources", "Data", "Textures"));
                destinations.Add(Path.Combine(playerPath, "Contents", "Data", "Textures"));
                destinations.Add(Path.Combine(playerPath, "Contents", "Resources", "Textures"));
                destinations.Add(Path.Combine(playerPath, "Contents", "Resources", "Data", "StreamingAssets", "Textures"));
            }
            else if (target == BuildTarget.StandaloneWindows64)
            {
                destinations.Add(Path.Combine(playerDirectory, $"{Path.GetFileNameWithoutExtension(playerPath)}_Data", "Textures"));
            }

            foreach (string destination in destinations)
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, recursive: true);
                }

                Directory.CreateDirectory(destination);
                int copiedFileCount = 0;
                foreach (string sourceFile in Directory.EnumerateFiles(
                             source,
                             "*",
                             SearchOption.AllDirectories))
                {
                    if (sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relativePath = Path.GetRelativePath(source, sourceFile);
                    string destinationFile = Path.Combine(destination, relativePath);
                    string destinationDirectory = Path.GetDirectoryName(destinationFile) ??
                        throw new InvalidOperationException(
                            $"Runtime texture destination has no parent: {destinationFile}");
                    Directory.CreateDirectory(destinationDirectory);
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                    copiedFileCount++;
                }

                Log($"Copied {copiedFileCount} runtime texture files to {destination}.");
            }
        }

        /// <summary>
        /// The macOS target architecture lives in the macOS build module.
        /// Resolve it reflectively so the editor assembly keeps compiling
        /// without a direct platform-module reference.
        /// </summary>
        private static void TrySetAppleSiliconArchitecture()
        {
            try
            {
                Type settings =
                    Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                    ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor")
                    ?? throw new InvalidOperationException(
                        "Unity macOS build module does not expose UserBuildSettings.");

                var property = settings.GetProperty("architecture") ??
                    throw new InvalidOperationException(
                        "Unity macOS build settings do not expose architecture.");

                // MacOSArchitecture enum: x64 = 0, ARM64 = 1, x64ARM64 (Universal) = 2.
                property.SetValue(null, Enum.ToObject(property.PropertyType, 1));
                Log("macOS target architecture set to Apple Silicon (ARM64).");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Failed to configure the required Apple Silicon macOS build architecture.",
                    exception);
            }
        }

        private static void Log(string message) => Debug.Log($"[BuildScript] {message}");

        private static void Fail(string message)
        {
            Debug.LogError($"[BuildScript] {message}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
