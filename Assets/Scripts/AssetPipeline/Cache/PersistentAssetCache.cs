#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using UnityEngine;
using VContainer;

namespace Kern;

public interface IPersistentAssetCache
{
    UniTask<byte[]?> GetAssetAsync(string filename);

    UniTask SaveAssetAsync(string filename, byte[] data, string etag);

    UniTask<string?> GetETagAsync(string filename);

    bool HasAsset(string filename);

    void RemoveAsset(string filename);

    string GetAssetPath(string filename);
}

public sealed class PersistentAssetCache : IPersistentAssetCache
{
    private const string ManifestSuffix = ".entry";
    private const long DiskMaintenanceIntervalTicks = TimeSpan.TicksPerSecond * 30;
    private readonly string _cachePath;
    private readonly IAsyncOperationSupervisor? _operations;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _diskMaintenanceGate = new(1, 1);
    private long _nextDiskMaintenanceUtcTicks;

    [Inject]
    public PersistentAssetCache(IAsyncOperationSupervisor operations)
        : this(GetDefaultCachePath(), operations)
    {
    }

    internal PersistentAssetCache(string cachePath, IAsyncOperationSupervisor? operations = null)
    {
        _cachePath = Path.GetFullPath(cachePath);
        _operations = operations;
        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);
#if UNITY_IOS && !UNITY_EDITOR
        // Кэш скачивается заново с CDN: в резервную копию iCloud ему нельзя,
        // иначе он съедает квоту игрока и нарушает правила App Store.
        UnityEngine.iOS.Device.SetNoBackupFlag(_cachePath);
#endif
    }

    private static string GetDefaultCachePath()
    {
        string persistentPath = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentPath))
        {
            throw new InvalidOperationException(
                "Application.persistentDataPath is required for the persistent asset cache.");
        }

        string? parentPath = Path.GetDirectoryName(persistentPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"Persistent data parent directory '{parentPath}' does not exist.");
        }

        return Path.Combine(persistentPath, "AssetCache");
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    public byte[]? GetAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            byte[]? payload = ReadVerifiedAsset(assetPath, out _);
            if (payload != null)
            {
                Touch(assetPath);
            }

            return payload;
        }
        finally
        {
            gate.Release();
        }
    }

    public async UniTask<byte[]?> GetAssetAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        byte[]? payload;
        await gate.WaitAsync();
        try
        {
            (payload, _) = await ReadVerifiedAssetAsync(assetPath);
            if (payload != null)
            {
                Touch(assetPath);
            }
        }
        finally
        {
            gate.Release();
        }

        await TrimDiskCacheAsync();
        return payload;
    }
    private static void SaveAssetCore(string assetPath, byte[] data, string etag)
    {
        string manifestPath = GetManifestPath(assetPath);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        WriteAtomically(assetPath, data);
        WriteAtomically(manifestPath, PersistentAssetCacheEntryManifest.Create(data, etag).Serialize());
    }

    public void SaveAsset(string filename, byte[] data, string etag)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            SaveAssetCore(assetPath, data, etag);
        }
        finally
        {
            gate.Release();
        }

        ScheduleDiskTrim();
    }

    public async UniTask SaveAssetAsync(string filename, byte[] data, string etag)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            await SaveAssetCoreAsync(assetPath, data, etag);
        }
        finally
        {
            gate.Release();
        }

        await TrimDiskCacheAsync();
    }

    private static async UniTask SaveAssetCoreAsync(string assetPath, byte[] data, string etag)
    {
        string manifestPath = GetManifestPath(assetPath);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        await WriteAtomicallyAsync(assetPath, data);
        await WriteAtomicallyAsync(
            manifestPath,
            PersistentAssetCacheEntryManifest.Create(data, etag).Serialize());
    }
    public async UniTask<string?> GetETagAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            (byte[]? payload, PersistentAssetCacheEntryManifest manifest) =
                await ReadVerifiedAssetAsync(assetPath);
            if (payload == null)
            {
                return null;
            }

            Touch(assetPath);
            return manifest.ETag;
        }
        finally
        {
            gate.Release();
        }
    }

    public string? GetETag(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            byte[]? payload = ReadVerifiedAsset(assetPath, out PersistentAssetCacheEntryManifest manifest);
            if (payload == null)
            {
                return null;
            }

            Touch(assetPath);
            return manifest.ETag;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool HasAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        string manifestPath = GetManifestPath(assetPath);
        return File.Exists(assetPath) && File.Exists(manifestPath);
    }

    public void RemoveAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            RemoveEntryFiles(assetPath);
        }
        finally
        {
            gate.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Private Helpers
    // ═══════════════════════════════════════════════════════════

    public string GetAssetPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        var relative = filename.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_cachePath, relative));
        var cacheRoot = Path.GetFullPath(_cachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Asset filename escapes the persistent cache directory.", nameof(filename));
        }

        return fullPath;
    }

    private SemaphoreSlim GetEntryGate(string assetPath) =>
        _entryGates.GetOrAdd(assetPath, _ => new SemaphoreSlim(1, 1));

    private static string GetManifestPath(string assetPath) => assetPath + ManifestSuffix;

    private async UniTask TrimDiskCacheAsync()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks))
        {
            return;
        }

        await UniTask.RunOnThreadPool(TryTrimDiskCache);
    }

    private void ScheduleDiskTrim()
    {
        if (_operations == null)
        {
            return;
        }

        try
        {
            _operations.Run(
                "persistent_asset_cache_trim",
                _ => UniTask.RunOnThreadPool(TryTrimDiskCache));
        }
        catch (ObjectDisposedException)
        {
            // Disk trimming is maintenance; a cache save remains valid during teardown.
        }
    }

    private void TryTrimDiskCache()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks) ||
            !_diskMaintenanceGate.Wait(0))
        {
            return;
        }

        try
        {
            nowUtcTicks = DateTime.UtcNow.Ticks;
            if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks))
            {
                return;
            }

            Interlocked.Exchange(
                ref _nextDiskMaintenanceUtcTicks,
                nowUtcTicks + DiskMaintenanceIntervalTicks);
            var entries = new System.Collections.Generic.List<(string AssetPath, string ManifestPath, long Size, DateTime AccessUtc)>();
            long totalBytes = 0;
            foreach (string manifestPath in Directory.EnumerateFiles(
                         _cachePath,
                         "*" + ManifestSuffix,
                         SearchOption.AllDirectories))
            {
                string assetPath = manifestPath[..^ManifestSuffix.Length];
                if (!File.Exists(assetPath))
                {
                    File.Delete(manifestPath);
                    continue;
                }

                long size = new FileInfo(assetPath).Length + new FileInfo(manifestPath).Length;
                totalBytes += size;
                entries.Add((assetPath, manifestPath, size, File.GetLastAccessTimeUtc(assetPath)));
            }

            if (totalBytes <= ProjectRuntimeContracts.AssetStreaming.PersistentAssetCacheCapacityBytes)
            {
                return;
            }

            entries.Sort(static (left, right) => left.AccessUtc.CompareTo(right.AccessUtc));
            foreach ((string assetPath, string manifestPath, long size, _) in entries)
            {
                if (totalBytes <= ProjectRuntimeContracts.AssetStreaming.PersistentAssetCacheCapacityBytes)
                {
                    break;
                }

                SemaphoreSlim entryGate = GetEntryGate(assetPath);
                if (!entryGate.Wait(0))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(assetPath) || !File.Exists(manifestPath))
                    {
                        continue;
                    }

                    File.Delete(assetPath);
                    File.Delete(manifestPath);
                    totalBytes -= size;
                }
                finally
                {
                    entryGate.Release();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"[PersistentAssetCache] Disk-cache trim failed: {exception.Message}");
        }
        finally
        {
            _diskMaintenanceGate.Release();
        }
    }

    private static void Touch(string assetPath)
    {
        try
        {
            File.SetLastAccessTimeUtc(assetPath, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Access timestamps are only an eviction hint.
        }
    }

    private static byte[]? ReadVerifiedAsset(
        string assetPath,
        out PersistentAssetCacheEntryManifest manifest)
    {
        manifest = default;
        if (!File.Exists(assetPath) || !TryReadManifest(assetPath, out manifest))
        {
            RemoveEntryFiles(assetPath);
            return null;
        }

        byte[] payload = File.ReadAllBytes(assetPath);
        if (manifest.Matches(payload))
        {
            return payload;
        }

        RemoveEntryFiles(assetPath);
        manifest = default;
        return null;
    }

    private static async UniTask<(byte[]? Payload, PersistentAssetCacheEntryManifest Manifest)> ReadVerifiedAssetAsync(
        string assetPath)
    {
        (bool manifestRead, PersistentAssetCacheEntryManifest manifest) =
            await TryReadManifestAsync(assetPath);
        if (!File.Exists(assetPath) || !manifestRead)
        {
            RemoveEntryFiles(assetPath);
            return (null, default);
        }

        byte[] payload = await File.ReadAllBytesAsync(assetPath).AsUniTask();
        if (manifest.Matches(payload))
        {
            return (payload, manifest);
        }

        RemoveEntryFiles(assetPath);
        return (null, default);
    }

    private static async UniTask<(bool Success, PersistentAssetCacheEntryManifest Manifest)> TryReadManifestAsync(
        string assetPath)
    {
        string manifestPath = GetManifestPath(assetPath);
        if (!File.Exists(manifestPath))
        {
            return (false, default);
        }

        string text = await File.ReadAllTextAsync(manifestPath).AsUniTask();
        bool success = PersistentAssetCacheEntryManifest.TryParse(
            text,
            out PersistentAssetCacheEntryManifest manifest);
        return (success, manifest);
    }

    private static bool TryReadManifest(
        string assetPath,
        out PersistentAssetCacheEntryManifest manifest)
    {
        string manifestPath = GetManifestPath(assetPath);
        if (!File.Exists(manifestPath))
        {
            manifest = default;
            return false;
        }

        return PersistentAssetCacheEntryManifest.TryParse(
            File.ReadAllText(manifestPath),
            out manifest);
    }

    private static void RemoveEntryFiles(string assetPath)
    {
        DeleteIfExists(assetPath);
        DeleteIfExists(GetManifestPath(assetPath));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteAtomically(string path, string value) =>
        WriteAtomically(path, Encoding.UTF8.GetBytes(value));

    private static async UniTask WriteAtomicallyAsync(string path, byte[] data)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, data).AsUniTask();
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static UniTask WriteAtomicallyAsync(string path, string value) =>
        WriteAtomicallyAsync(path, Encoding.UTF8.GetBytes(value));

    private static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }
}
