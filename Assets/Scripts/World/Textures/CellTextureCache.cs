#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Manages caching of cell textures for efficient loading and memory management.
/// </summary>
public class CellTextureCache
{
    private readonly ConcurrentDictionary<CellType, CellTextureInfo> _textureCache = new();
    private readonly ConcurrentDictionary<CellType, Texture2D> _loadedTextures = new();
    private readonly ConcurrentDictionary<string, CellType> _filenameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Add a texture to the cache.
    /// </summary>
    /// <param name="cellType">The cell type.</param>
    /// <param name="textureInfo">Texture information.</param>
    public void AddTexture(CellType cellType, CellTextureInfo textureInfo)
    {
        if (_textureCache.TryGetValue(cellType, out CellTextureInfo previous) &&
            previous.OwnsBaseTexture &&
            previous.BaseTexture != textureInfo.BaseTexture)
        {
            DestroyTexture(previous.BaseTexture);
        }

        _textureCache.AddOrUpdate(cellType, textureInfo, (key, oldValue) => textureInfo);
        _loadedTextures.AddOrUpdate(cellType, textureInfo.BaseTexture, (key, oldValue) => textureInfo.BaseTexture);

        // Cache filename mapping
        var filename = $"Cells/{(int)cellType}";
        _filenameCache.TryAdd(filename, cellType);
    }

    /// <summary>
    /// Try to get texture information from cache.
    /// </summary>
    /// <param name="cellType">The cell type.</param>
    /// <param name="textureInfo">Output texture information.</param>
    /// <returns>True if found, false otherwise.</returns>
    public bool TryGetTexture(CellType cellType, out CellTextureInfo textureInfo) =>
        _textureCache.TryGetValue(cellType, out textureInfo);

    /// <summary>
    /// Get a cached texture for a cell type.
    /// </summary>
    /// <param name="cellType">The cell type.</param>
    /// <returns>The cached texture or null if not found.</returns>
    public Texture2D? GetCachedTexture(CellType cellType) =>
        _loadedTextures.TryGetValue(cellType, out var texture) ? texture : null;

    /// <summary>
    /// Clear all cached textures.
    /// </summary>
    public void Clear()
    {
        HashSet<Texture2D> ownedTextures = [];
        foreach (CellTextureInfo textureInfo in _textureCache.Values)
        {
            if (textureInfo.OwnsBaseTexture && textureInfo.BaseTexture != null)
            {
                ownedTextures.Add(textureInfo.BaseTexture);
            }
        }

        _textureCache.Clear();
        _loadedTextures.Clear();
        _filenameCache.Clear();
        foreach (Texture2D texture in ownedTextures)
        {
            DestroyTexture(texture);
        }
    }

    /// <summary>
    /// Get memory usage of cached textures.
    /// </summary>
    /// <returns>Approximate memory usage in bytes.</returns>
    public long GetMemoryUsage()
    {
        long totalSize = 0;

        foreach (var texture in _loadedTextures.Values)
        {
            if (texture != null)
            {
                // Approximate texture memory usage (width * height * bytes per pixel)
                totalSize += texture.width * texture.height * 4; // RGBA32 = 4 bytes per pixel
            }
        }

        return totalSize;
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    /// <returns>Cache statistics string.</returns>
    public string GetCacheStats() =>
        $"Cache: {_textureCache.Count} textures, {GetMemoryUsage() / 1024} KB";

    /// <summary>
    /// Try to parse cell type from filename.
    /// </summary>
    /// <param name="filename">The filename to parse.</param>
    /// <param name="cellType">Output cell type.</param>
    /// <returns>True if successfully parsed, false otherwise.</returns>
    private static bool TryParseCellTypeFromFilename(string filename, out CellType cellType)
    {
        cellType = CellType.Unloaded;

        // Extract cell ID from filenames such as "Cells/50".
        if (filename.StartsWith("Cells/", StringComparison.OrdinalIgnoreCase))
        {
            string idStr = filename.Substring(6);

            int dotIndex = idStr.LastIndexOf('.');
            if (dotIndex > 0)
            {
                idStr = idStr.Substring(0, dotIndex);
            }

            if (int.TryParse(idStr, out int cellId) &&
                Enum.IsDefined(typeof(CellType), cellId))
            {
                cellType = (CellType)cellId;
                return true;
            }
        }

        return false;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
