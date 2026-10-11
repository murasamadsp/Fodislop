#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public static class WorldMapArchiveUpgrader
{
    private const string ArchiveDirectory = "Assets/StreamingAssets/WorldMaps";
    private const string WorkingDirectory = "Library/KernBuild/WorldMapUpgrade";

    [MenuItem("Kern/World/Upgrade Packaged Map Archives")]
    public static void UpgradePackagedArchives()
    {
        string archiveDirectory = Path.GetFullPath(ArchiveDirectory);
        if (!Directory.Exists(archiveDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Packaged world-map directory '{archiveDirectory}' does not exist.");
        }

        string workingDirectory = Path.GetFullPath(WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);

        int upgraded = 0;
        foreach (string archivePath in Directory.EnumerateFiles(
            archiveDirectory,
            "*.zip",
            SearchOption.TopDirectoryOnly))
        {
            if (UpgradeArchive(archivePath, workingDirectory))
            {
                upgraded++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[WorldMapArchiveUpgrader] Upgraded {upgraded} packaged map archive(s).");
    }

    private static bool UpgradeArchive(string archivePath, string workingDirectory)
    {
        string archiveName = Path.GetFileNameWithoutExtension(archivePath);
        string mapPath = Path.Combine(workingDirectory, archiveName + ".mapb");
        string upgradedArchivePath = archivePath + ".upgrade.tmp";
        string? targetEntryName = null;

        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                ZipArchiveEntry? mapEntry = null;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("_cells.mapb", StringComparison.Ordinal))
                    {
                        if (mapEntry != null)
                        {
                            throw new InvalidDataException(
                                $"Archive '{archivePath}' contains more than one cell map.");
                        }

                        mapEntry = entry;
                    }
                }

                if (mapEntry == null)
                {
                    throw new InvalidDataException(
                        $"Archive '{archivePath}' does not contain a '*_cells.mapb' entry.");
                }

                targetEntryName = mapEntry.FullName;
                using Stream input = mapEntry.Open();
                using var output = new FileStream(mapPath, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }

            int widthChunks;
            int heightChunks;
            int chunkSize;
            int formatVersion;
            using (var map = new FileStream(mapPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(map))
            {
                widthChunks = reader.ReadInt32();
                heightChunks = reader.ReadInt32();
                chunkSize = reader.ReadInt32();
                formatVersion = reader.ReadInt32();
            }

            if (formatVersion == WorldLayerFileHeader.CurrentFormatVersion)
            {
                return false;
            }

            if (formatVersion > WorldLayerFileHeader.CurrentFormatVersion || formatVersion < 0)
            {
                throw new InvalidDataException(
                    $"Archive '{archivePath}' uses unsupported map format {formatVersion}.");
            }

            if (widthChunks <= 0 || heightChunks <= 0 || chunkSize != ProjectRuntimeContracts.World.ChunkSize)
            {
                throw new InvalidDataException(
                    $"Archive '{archivePath}' has an invalid map header: " +
                    $"{widthChunks}x{heightChunks}, chunkSize={chunkSize}, version={formatVersion}.");
            }

            Task.Run(() => WorldLayer<CellType>.MigrateLegacyFileSynchronously(
                    mapPath,
                    widthChunks,
                    heightChunks,
                    chunkSize))
                .GetAwaiter()
                .GetResult();

            string requiredEntryName = targetEntryName ??
                throw new InvalidOperationException("The target cell-map entry was not selected.");
            using (ZipArchive source = ZipFile.OpenRead(archivePath))
            using (var outputStream = new FileStream(
                upgradedArchivePath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None))
            using (var destination = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (ZipArchiveEntry entry in source.Entries)
                {
                    ZipArchiveEntry upgradedEntry = destination.CreateEntry(
                        entry.FullName,
                        System.IO.Compression.CompressionLevel.Optimal);
                    upgradedEntry.LastWriteTime = entry.LastWriteTime;
                    using Stream target = upgradedEntry.Open();
                    if (string.Equals(entry.FullName, requiredEntryName, StringComparison.Ordinal))
                    {
                        using var convertedMap = new FileStream(mapPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        convertedMap.CopyTo(target);
                    }
                    else
                    {
                        using Stream original = entry.Open();
                        original.CopyTo(target);
                    }
                }
            }

            File.Replace(upgradedArchivePath, archivePath, destinationBackupFileName: null);
            Debug.Log(
                $"[WorldMapArchiveUpgrader] Upgraded '{archivePath}' from format {formatVersion} " +
                $"to format {WorldLayerFileHeader.CurrentFormatVersion} " +
                $"({widthChunks}x{heightChunks} chunks).");
            return true;
        }
        finally
        {
            DeleteIfPresent(mapPath);
            DeleteIfPresent(mapPath + ".migrate.tmp");
            DeleteIfPresent(mapPath + ".v3.migrate.tmp");
            DeleteIfPresent(mapPath + ".v1.backup");
            DeleteIfPresent(mapPath + ".v2.backup");
            DeleteIfPresent(upgradedArchivePath);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

}
