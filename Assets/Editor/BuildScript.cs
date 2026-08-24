#if UNITY_EDITOR
using System;
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
    /// Menu: Build > macOS (Apple Silicon) / Windows 64 / Linux 64 / Android / iOS.
    /// Output goes to Build/&lt;platform&gt;/ (gitignored).
    ///
    /// Сборка сама себя проверяет и чинит перед запуском:
    ///   • список сцен приводится к каноническому (BuildSettingsFix) — сцена,
    ///     выпавшая из Build Settings, ломается только в билде и незаметна
    ///     при разработке, потому что сцены грузятся аддитивно по имени;
    ///   • активная платформа переключается на целевую, иначе Unity молча
    ///     собирает не то и тратит на это полный реимпорт;
    ///   • каталог вывода очищается, чтобы файлы прошлой сборки не уезжали
    ///     в новую.
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

        [MenuItem("Build/Linux 64")]
        public static void BuildLinux() =>
            Build(BuildTarget.StandaloneLinux64, $"Build/Linux/{ProductName}");

        [MenuItem("Build/Android APK")]
        public static void BuildAndroid() =>
            Build(BuildTarget.Android, $"Build/Android/{ProductName}.apk");

        [MenuItem("Build/iOS Xcode Project")]
        public static void BuildIOS() =>
            Build(BuildTarget.iOS, "Build/iOS");

        private static void Build(BuildTarget target, string relativeOutput, bool isApple = false)
        {
            // Чинить список сцен до сборки, а не надеяться, что он верен:
            // отсутствующая сцена проявится только в готовом билде.
            BuildSettingsFix.EnsureScenesInBuildSettings();

            var scenes = EnabledScenes;
            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in EditorBuildSettings — nothing to build.");
                return;
            }

            if (!EnsureActiveBuildTarget(target))
            {
                return;
            }

            string output = Path.GetFullPath(relativeOutput);
            string outputDirectory = Path.GetDirectoryName(output)
                ?? throw new InvalidOperationException($"Build output has no parent directory: {output}");

            CleanOutput(output, outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            if (isApple)
            {
                TrySetAppleSiliconArchitecture();
            }

            bool development = Environment.GetCommandLineArgs().Contains(DevArg);

            try
            {
                UnityEditor.Build.NamedBuildTarget namedTarget =
                    UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                        BuildPipeline.GetBuildTargetGroup(target));

                // Master компилируется в разы дольше и не даёт ничего отладке.
                // Раньше он ставился и для development-сборок тоже.
                PlayerSettings.SetIl2CppCompilerConfiguration(
                    namedTarget,
                    development ? Il2CppCompilerConfiguration.Debug : Il2CppCompilerConfiguration.Master);
                PlayerSettings.SetIl2CppCodeGeneration(
                    namedTarget,
                    UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);

                // Minimal, а не Medium/High: в проекте VContainer, а он резолвит
                // типы рефлексией — агрессивный стриппинг вырезает то, на что
                // нет статических ссылок, и DI падает только в билде.
                PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Minimal);
            }
            catch (Exception ex)
            {
                Log($"Optimization settings notice: {ex.Message}");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = development
                    // ConnectWithProfiler — без него профайлер к билду не
                    // цепляется, хотя AllowDebugging создаёт впечатление, что
                    // всё включено.
                    ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler
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

            Log($"Build succeeded: {output}");
            Log($"Версия {PlayerSettings.bundleVersion}{(development ? " (development)" : string.Empty)}");
            Log($"Запуск: {LaunchHint(target, output)}");
        }

        /// <summary>Готовая команда запуска — чтобы не искать бинарник руками.</summary>
        private static string LaunchHint(BuildTarget target, string output) => target switch
        {
            BuildTarget.StandaloneOSX => $"open \"{output}\"",
            BuildTarget.StandaloneWindows64 => $"\"{output}\"",
            BuildTarget.StandaloneLinux64 => $"\"{output}\"",
            BuildTarget.Android => $"adb install -r \"{output}\"",
            BuildTarget.iOS => $"open \"{output}\"",
            _ => output,
        };

        /// <summary>
        /// Переключает активную платформу на целевую. Без этого Unity собирает
        /// плеер для текущей платформы независимо от того, что просили, —
        /// и обнаруживается это уже по нерабочему бинарнику.
        /// </summary>
        private static bool EnsureActiveBuildTarget(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                return true;
            }

            var group = BuildPipeline.GetBuildTargetGroup(target);
            Log($"Переключаю платформу: {EditorUserBuildSettings.activeBuildTarget} → {target} (это перезапустит импорт ассетов).");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                Fail($"Не удалось переключиться на {target}. Модуль платформы установлен?");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Удаляет прошлый билд. Инкрементальная сборка Unity не убирает файлы,
        /// которые перестали быть нужны, — старые текстуры и библиотеки уезжают
        /// в новый билд и маскируют то, что на самом деле сломано.
        /// </summary>
        private static void CleanOutput(string output, string outputDirectory)
        {
            try
            {
                if (Directory.Exists(output))
                {
                    Directory.Delete(output, recursive: true);
                }
                else if (File.Exists(output))
                {
                    File.Delete(output);
                    string dataDirectory = Path.Combine(
                        outputDirectory,
                        $"{Path.GetFileNameWithoutExtension(output)}_Data");
                    if (Directory.Exists(dataDirectory))
                    {
                        Directory.Delete(dataDirectory, recursive: true);
                    }
                }
            }
            catch (Exception exception)
            {
                Log($"Прошлый билд удалить не удалось ({exception.Message}); собираю поверх.");
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
