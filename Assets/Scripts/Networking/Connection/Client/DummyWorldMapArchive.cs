#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Fodinae.Networking.Connection.Client;

internal static class DummyWorldMapArchive
{
    public static string ResolveMapFile(string worldCodeName)
    {
        string streamingDirectory = Path.Combine(Application.streamingAssetsPath, "WorldMaps");
        string projectMapPath = Path.Combine(streamingDirectory, $"{worldCodeName}_cells.mapb");
        if (File.Exists(projectMapPath))
        {
            return projectMapPath;
        }

        string projectArchivePath = Path.Combine(streamingDirectory, $"{worldCodeName}_cells.zip");
        if (!File.Exists(projectArchivePath))
        {
            throw new FileNotFoundException(
                $"Dummy server map '{worldCodeName}' is missing both the mapb file and its zip archive.",
                projectMapPath);
        }

        try
        {
            string cacheDirectory = Path.Combine(Application.temporaryCachePath, "DummyServerMaps");
            Directory.CreateDirectory(cacheDirectory);
            string cachedMapPath = Path.Combine(cacheDirectory, $"{worldCodeName}_cells.mapb");
            using ZipArchive archive = ZipFile.OpenRead(projectArchivePath);
            ZipArchiveEntry? mapEntry = archive.GetEntry($"{worldCodeName}_cells.mapb");
            if (mapEntry == null)
            {
                throw new InvalidDataException(
                    $"Dummy server archive '{projectArchivePath}' does not contain " +
                    $"'{worldCodeName}_cells.mapb'.");
            }

            var cachedInfo = new FileInfo(cachedMapPath);
            if (!cachedInfo.Exists ||
                cachedInfo.Length != mapEntry.Length ||
                cachedInfo.LastWriteTimeUtc != mapEntry.LastWriteTime.UtcDateTime)
            {
                mapEntry.ExtractToFile(cachedMapPath, overwrite: true);
            }

            return cachedMapPath;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to open dummy server map '{worldCodeName}'.", ex);
        }
    }

    public static (int width, int height) ReadDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            int widthChunks = reader.ReadInt32();
            int heightChunks = reader.ReadInt32();
            int chunkSize = reader.ReadInt32();
            reader.ReadInt32();

            if (widthChunks > 0 && heightChunks > 0 && chunkSize > 0 && chunkSize <= 1024)
            {
                return (widthChunks * chunkSize, heightChunks * chunkSize);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed reading prebaked map dimensions from '{path}'.", ex);
        }

        throw new InvalidDataException($"Prebaked map '{path}' contains invalid dimensions.");
    }
}
