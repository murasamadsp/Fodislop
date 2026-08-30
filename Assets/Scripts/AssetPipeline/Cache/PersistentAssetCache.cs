#nullable enable

using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae;

public static class PersistentAssetCache
{
    private static string _cachePath = string.Empty;
    private static bool _isInitialized;

    static PersistentAssetCache()
    {
        InitializeCachePath();
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    public static byte[]? GetAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        if (File.Exists(assetPath))
        {
            return File.ReadAllBytes(assetPath);
        }

        return null;
    }

    public static async UniTask<byte[]?> GetAssetAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        if (File.Exists(assetPath))
        {
            return await File.ReadAllBytesAsync(assetPath).AsUniTask();
        }

        return null;
    }

    public static void SaveAsset(string filename, byte[] data, string etag)
    {
        InitializeCachePath();

        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        string etagPath = GetETagPath(filename);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        WriteAtomically(assetPath, data);
        WriteAtomically(etagPath, etag);
    }

    public static async UniTask SaveAssetAsync(string filename, byte[] data, string etag)
    {
        InitializeCachePath();

        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        string etagPath = GetETagPath(filename);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        await WriteAtomicallyAsync(assetPath, data);
        await WriteAtomicallyAsync(etagPath, etag);
    }

    public static string? GetETag(string filename)
    {
        string etagPath = GetETagPath(filename);
        if (File.Exists(etagPath))
        {
            return File.ReadAllText(etagPath);
        }

        return null;
    }

    public static async UniTask<string?> GetETagAsync(string filename)
    {
        string etagPath = GetETagPath(filename);
        if (File.Exists(etagPath))
        {
            return await File.ReadAllTextAsync(etagPath).AsUniTask();
        }

        return null;
    }

    public static bool HasAsset(string filename)
    {
        return File.Exists(GetAssetPath(filename));
    }

    public static void RemoveAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        string etagPath = GetETagPath(filename);
        if (File.Exists(assetPath))
        {
            File.Delete(assetPath);
        }

        if (File.Exists(etagPath))
        {
            File.Delete(etagPath);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Private Helpers
    // ═══════════════════════════════════════════════════════════

    private static void InitializeCachePath()
    {
        if (_isInitialized)
        {
            return;
        }

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

        _cachePath = Path.Combine(persistentPath, "AssetCache");
        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);
        _isInitialized = true;
    }

    public static string GetAssetPath(string filename)
    {
        InitializeCachePath();

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

    private static string GetETagPath(string filename) => GetAssetPath(filename) + ".etag";

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
