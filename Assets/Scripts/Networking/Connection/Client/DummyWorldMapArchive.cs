#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyWorldMapArchive
{
    /// <summary>
    /// Resolves the map file for a world, extracting it from the zip archive
    /// off the main thread when a cache miss occurs.
    ///
    /// The bundled map archives are tens of megabytes (pallada_cells.zip is
    /// ~78 MB); extracting one synchronously would stall the main thread for
    /// seconds right after world startup. The fast path (a previously
    /// extracted .mapb file) returns synchronously.
    /// </summary>
    public static async UniTask<string> ResolveMapFileAsync(string worldCodeName)
    {
        // Application.* path properties are main-thread-only; capture them
        // before the thread-pool hop so the extraction never touches Unity
        // APIs off the main thread.
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

        string cacheDirectory = Path.Combine(Application.temporaryCachePath, "DummyServerMaps");
        return await UniTask.RunOnThreadPool(
            () => ExtractFromArchive(projectArchivePath, cacheDirectory, worldCodeName));
    }

    private static string ExtractFromArchive(
        string projectArchivePath,
        string cacheDirectory,
        string worldCodeName)
    {
        try
        {
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

            throw new InvalidDataException(
                $"Dummy map '{path}' has invalid header ({widthChunks}x{heightChunks}, chunk {chunkSize}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidDataException($"Failed to read dummy map header '{path}'.", ex);
        }
    }
}
