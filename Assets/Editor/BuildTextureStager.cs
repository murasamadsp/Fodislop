#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Copies Assets/Textures into the player's data folder so runtime texture
    /// lookup (CellTextureCache / TextureStorageManager / ItemRegistry) finds
    /// bundled assets in a standalone build. In the Editor Application.dataPath
    /// points at the project's Assets folder, which masks this gap; a player
    /// build must carry the same tree under &lt;game&gt;_Data/Textures.
    /// </summary>
    public class BuildTextureStager : IPostprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPostprocessBuild(BuildReport report)
        {
            string outputPath = report.summary.outputPath;
            string source = Path.Combine(Application.dataPath, "Textures");
            if (!Directory.Exists(source))
            {
                Debug.LogError($"[BuildTextureStager] Bundled textures source not found: {source}");
                return;
            }

            string dataFolder = ResolveDataFolder(outputPath);
            if (string.IsNullOrEmpty(dataFolder))
            {
                Debug.LogError($"[BuildTextureStager] Cannot resolve data folder for build output: {outputPath}");
                return;
            }

            string target = Path.Combine(dataFolder, "Textures");
            int copied = CopyDirectoryRecursive(source, target);
            Debug.Log($"[BuildTextureStager] Copied {copied} texture file(s): {source} -> {target}");
        }

        private static string? ResolveDataFolder(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                return null;
            }

            if (outputPath.EndsWith(".app", StringComparison.Ordinal))
            {
                string macData = Path.Combine(outputPath, "Contents", "Resources", "Data");
                return Directory.Exists(macData) ? macData : null;
            }

            string exeName = Path.GetFileName(outputPath);
            string? directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(exeName) || string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string dataFolder = Path.Combine(directory, Path.GetFileNameWithoutExtension(exeName) + "_Data");
            return Directory.Exists(dataFolder) ? dataFolder : null;
        }

        private static int CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            int copied = 0;

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
                copied++;
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                copied += CopyDirectoryRecursive(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
            }

            return copied;
        }
    }
}
#endif