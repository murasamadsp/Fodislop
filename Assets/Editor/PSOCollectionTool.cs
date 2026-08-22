#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Editor
{
    /// <summary>
    /// Сохраняет отслеженную Pipeline State Object коллекцию в ассет и позволяет
    /// сбросить запись, чтобы начать сбор с чистого листа.
    ///
    /// Flow (Save): играть в Play Mode с включённой трассировкой (Project Settings →
    /// Graphics → PSO Collection, Cache Miss Tracing = on), выйти из Play Mode —
    /// редактор записывает собранную коллекцию в файл .graphicsstate — затем
    /// выполнить Fodinae > PSO > Save Traced Collection. Ассет импортируется и
    /// назначается в Project Settings → Graphics со Startup Action = Warm Up All
    /// (или назначается вручную через m_GraphicsStateCollection /
    /// m_CollectionStartupAction в ProjectSettings/GraphicsSettings.asset).
    ///
    /// Flow (Reset): Fodinae > PSO > Reset Traced Collection удаляет все
    /// отслеженные файлы .graphicsstate и очищает cache-miss коллекцию
    /// назначенного ассета, чтобы следующая запись началась с нуля.
    ///
    /// Note: UnityEditor.Rendering.GraphicsStateCollectionImporter (класс, который
    /// импортирует файлы .graphicsstate) internal и недоступен из пользовательского
    /// кода, а его статический метод лишь создаёт пустой ассет. Поэтому файл
    /// импортируется через AssetDatabase (тот же ScriptedImporter), а результат
    /// подключается к Graphics settings через сериализованные поля.
    /// </summary>
    internal static class PSOCollectionTool
    {
        private const string GraphicsSettingsAssetPath = "ProjectSettings/GraphicsSettings.asset";
        private const string CollectionExtension = ".graphicsstate";

        // Serialized fields of the Graphics settings asset (Unity 6 PSO collection settings).
        private const string TraceSavePathProperty = "m_TraceSavePath";
        private const string CacheMissCollectionPathProperty = "m_CacheMissCollectionPath";
        private const string EnableCacheMissTracingProperty = "m_EnableCacheMissTracing";
        private const string GraphicsStateCollectionProperty = "m_GraphicsStateCollection";
        private const string CollectionStartupActionProperty = "m_CollectionStartupAction";

        // GraphicsStateCollectionStartupAction is internal; it serializes as an int:
        // 0 = None, 1 = BeginTrace, 2 = Warmup (Warm Up All).
        private const int StartupActionWarmup = 2;

        [MenuItem("Fodinae/PSO/Save Traced Collection")]
        private static void SaveTracedCollection()
        {
            try
            {
                SaveTracedCollectionInternal();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PSO] Failed to save traced collection: {exception}");
            }
        }

        [MenuItem("Fodinae/PSO/Reset Traced Collection", false, 100)]
        private static void ResetTracedCollection()
        {
            try
            {
                ResetTracedCollectionInternal();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PSO] Failed to reset traced collection: {exception}");
            }
        }

        private static void SaveTracedCollectionInternal()
        {
            var settings = LoadGraphicsSettings();
            if (settings == null)
            {
                return;
            }

            using (var serializedSettings = new SerializedObject(settings))
            {
                WarnIfTracingDisabled(serializedSettings);

                var files = CollectTracedFiles(serializedSettings);
                if (files.Count == 0)
                {
                    Debug.LogError(
                        "[PSO] No traced collection found. Flow: enable Cache Miss Tracing in Project Settings → Graphics, " +
                        "enter Play Mode and exercise the rendering you want to capture, exit Play Mode, then run " +
                        "Fodinae > PSO > Save Traced Collection again.");
                    return;
                }

                string assetPath = files[0]; // newest first
                ImportTracedCollection(serializedSettings, assetPath);
            }
        }

        private static void ResetTracedCollectionInternal()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[PSO] Cannot reset while Play Mode is running — exit Play Mode first.");
                return;
            }

            var settings = LoadGraphicsSettings();
            if (settings == null)
            {
                return;
            }

            using (var serializedSettings = new SerializedObject(settings))
            {
                var files = CollectTracedFiles(serializedSettings);
                if (files.Count == 0)
                {
                    Debug.Log("[PSO] No traced collection files found to delete.");
                    ClearCacheMissCollection(serializedSettings);
                    return;
                }

                string fileList = string.Join("\n", files.Take(5).Select(f => "• " + f));
                if (files.Count > 5)
                {
                    fileList += $"\n… and {files.Count - 5} more";
                }

                string message = $"Delete {files.Count} traced collection file(s)?\n\n{fileList}\n\n" +
                                 "The reference in Project Settings → Graphics will be missing until a new capture is saved.";
                bool confirmed = EditorUtility.DisplayDialog("Reset Traced Collection", message, "Reset", "Cancel");
                if (!confirmed)
                {
                    Debug.Log("[PSO] Reset cancelled.");
                    return;
                }

                foreach (string path in files)
                {
                    if (!AssetDatabase.DeleteAsset(path))
                    {
                        Debug.LogWarning($"[PSO] Could not delete {path}");
                    }
                }

                ClearCacheMissCollection(serializedSettings);
                AssetDatabase.Refresh();
                Debug.Log($"[PSO] Reset done: deleted {files.Count} traced collection file(s), cache-miss collection cleared.");
            }
        }

        private static void ImportTracedCollection(SerializedObject serializedSettings, string assetPath)
        {
            // Import the .graphicsstate file (triggers the internal
            // GraphicsStateCollectionImporter) so it becomes an asset.
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var collection = AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>(assetPath);
            if (collection == null)
            {
                Debug.LogError($"[PSO] Imported {assetPath} but no GraphicsStateCollection was produced.");
                return;
            }

            Debug.Log(
                $"[PSO] Traced collection ready at {assetPath} " +
                $"({collection.variantCount} variants, {collection.totalGraphicsStateCount} states).");

            if (EditorUtility.DisplayDialog(
                    "PSO Collection",
                    $"Use {assetPath} as the startup Graphics State Collection with Startup Action = Warm Up All?",
                    "Assign",
                    "Cancel"))
            {
                serializedSettings.FindProperty(GraphicsStateCollectionProperty).objectReferenceValue = collection;
                serializedSettings.FindProperty(CollectionStartupActionProperty).enumValueIndex = StartupActionWarmup;
                serializedSettings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[PSO] Assigned {assetPath} in Project Settings → Graphics, Startup Action = Warm Up All.");
            }
            else
            {
                Debug.Log(
                    $"[PSO] Traced collection saved as {assetPath}. Assign it in Project Settings → Graphics and " +
                    "set Startup Action = Warm Up All.");
            }
        }

        private static void ClearCacheMissCollection(SerializedObject serializedSettings)
        {
            var assigned = serializedSettings.FindProperty(GraphicsStateCollectionProperty)?.objectReferenceValue as GraphicsStateCollection;
            if (assigned == null)
            {
                Debug.Log(
                    "[PSO] No collection assigned in Graphics Settings — nothing to clear in-memory. " +
                    "The editor discards traced data when Play Mode ends.");
                return;
            }

            try
            {
                assigned.EraseCacheMissCollection();
                EditorUtility.SetDirty(assigned);
                Debug.Log($"[PSO] Cache-miss collection cleared on '{assigned.name}'.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PSO] Could not clear cache-miss collection: {exception.Message}");
            }
        }

        private static UnityEngine.Object? LoadGraphicsSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(GraphicsSettingsAssetPath);
            if (settings == null)
            {
                Debug.LogError($"[PSO] Could not load {GraphicsSettingsAssetPath}.");
            }

            return settings;
        }

        private static void WarnIfTracingDisabled(SerializedObject serializedSettings)
        {
            bool tracingEnabled = serializedSettings.FindProperty(EnableCacheMissTracingProperty)?.boolValue ?? false;
            if (!tracingEnabled)
            {
                Debug.LogWarning(
                    "[PSO] Cache Miss Tracing (m_EnableCacheMissTracing) is off in Project Settings → Graphics. " +
                    "Enable it, play the game, exit Play Mode, then run this item again.");
            }
        }

        private static List<string> CollectTracedFiles(SerializedObject serializedSettings)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(paths, serializedSettings.FindProperty(CacheMissCollectionPathProperty)?.stringValue ?? string.Empty);
            AddCandidate(paths, serializedSettings.FindProperty(TraceSavePathProperty)?.stringValue ?? string.Empty);

            // Also scan the whole project — the editor may save the traced
            // collection to a path other than the configured one.
            foreach (string fullPath in Directory.GetFiles(Application.dataPath, "*" + CollectionExtension, SearchOption.AllDirectories))
            {
                paths.Add("Assets/" + fullPath.Substring(Application.dataPath.Length + 1).Replace('\\', '/'));
            }

            return paths.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }

        private static void AddCandidate(ICollection<string> candidates, string configuredPath)
        {
            if (string.IsNullOrEmpty(configuredPath))
            {
                return;
            }

            string assetPath = configuredPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? configuredPath
                : "Assets/" + configuredPath.TrimStart('/');
            if (!assetPath.EndsWith(CollectionExtension, StringComparison.OrdinalIgnoreCase))
            {
                assetPath += CollectionExtension;
            }

            candidates.Add(assetPath);
        }
    }
}
#endif
