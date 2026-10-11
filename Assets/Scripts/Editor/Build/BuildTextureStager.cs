#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Build;

namespace Kern.Editor;

public sealed class BuildTextureStager : BuildPlayerProcessor
{
    private const string StagingRoot = "Library/KernBuild/StreamingAssets";

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        string source = Path.GetFullPath(Path.Combine("Assets", "Textures"));
        if (!Directory.Exists(source))
        {
            throw new BuildFailedException(
                $"Required runtime texture directory does not exist: {source}");
        }

        string stagingRoot = Path.GetFullPath(StagingRoot);
        string stagedTextures = Path.Combine(stagingRoot, "Textures");
        if (Directory.Exists(stagedTextures))
        {
            Directory.Delete(stagedTextures, recursive: true);
        }

        CopyRuntimeFiles(source, stagedTextures);
        string[] relativeFiles = Directory
            .EnumerateFiles(stagedTextures, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stagedTextures, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string manifestPath = Path.Combine(stagingRoot, "Textures.manifest");
        File.WriteAllLines(manifestPath, relativeFiles);

        buildPlayerContext.AddAdditionalPathToStreamingAssets(stagedTextures, "Textures");
        buildPlayerContext.AddAdditionalPathToStreamingAssets(manifestPath, "Textures.manifest");

        string worldMaps = Path.GetFullPath(Path.Combine("Assets", "StreamingAssets", "WorldMaps"));
        if (Directory.Exists(worldMaps))
        {
            string[] archiveEntries = Directory.EnumerateFiles(worldMaps, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Select(path => $"{Path.GetFileName(path)}|{ComputeSha256(path)}")
                .ToArray();
            string mapManifest = Path.Combine(stagingRoot, "WorldMaps.manifest");
            File.WriteAllLines(mapManifest, archiveEntries);
            buildPlayerContext.AddAdditionalPathToStreamingAssets(mapManifest, "WorldMaps/WorldMaps.manifest");
        }
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static void CopyRuntimeFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string sourceFile in Directory.EnumerateFiles(source))
        {
            // .meta — метаданные Unity, а файлы с точки впереди (.DS_Store и
            // подобный мусор Finder) в пакет не попадают: манифест, собранный
            // по каталогу стейджинга, тогда ссылался бы на то, чего в APK нет,
            // и распаковка на устройстве падала бы с 404.
            string fileName = Path.GetFileName(sourceFile);
            if (sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith('.'))
            {
                continue;
            }

            File.Copy(
                sourceFile,
                Path.Combine(destination, fileName),
                overwrite: true);
        }

        foreach (string sourceDirectory in Directory.EnumerateDirectories(source))
        {
            CopyRuntimeFiles(
                sourceDirectory,
                Path.Combine(destination, Path.GetFileName(sourceDirectory)));
        }
    }
}
