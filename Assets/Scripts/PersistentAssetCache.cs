
using UnityEngine;
using System.IO;

public static class PersistentAssetCache
{
    private static string _cachePath = Path.Combine(Application.persistentDataPath, "AssetCache");

    static PersistentAssetCache()
    {
        if (!Directory.Exists(_cachePath))
        {
            Directory.CreateDirectory(_cachePath);
        }
    }

    private static string GetAssetPath(string filename) => Path.Combine(_cachePath, filename);
    private static string GetETagPath(string filename) => Path.Combine(_cachePath, filename + ".etag");

    public static byte[] GetAsset(string filename)
    {
        var assetPath = GetAssetPath(filename);
        if (File.Exists(assetPath))
        {
            return File.ReadAllBytes(assetPath);
        }
        return null;
    }

    public static void SaveAsset(string filename, byte[] data, string etag)
    {
        File.WriteAllBytes(GetAssetPath(filename), data);
        File.WriteAllText(GetETagPath(filename), etag);
    }

    public static string GetETag(string filename)
    {
        var etagPath = GetETagPath(filename);
        if (File.Exists(etagPath))
        {
            return File.ReadAllText(etagPath);
        }
        return null;
    }

    public static bool HasAsset(string filename)
    {
        return File.Exists(GetAssetPath(filename));
    }
}
