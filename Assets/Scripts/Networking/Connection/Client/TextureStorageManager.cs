#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Networking.Connection.Client
{
    /// <summary>
    /// Manager for storing and caching textures downloaded from the server or loaded locally.
    /// Provides thread-safe async access with in-memory caching.
    /// Writes downloaded assets to persistentDataPath to prevent Unity AssetDatabase reloads in Editor.
    /// </summary>
    public class TextureStorageManager : MonoBehaviour, ITextureStorageService
    {
        [SerializeField]
        private bool _enableDebugLogging = false;

        private readonly ConcurrentDictionary<string, Texture2D> _textureCache = new();
        private readonly ConcurrentDictionary<string, string> _resolvedPathsCache = new();

        private string? _textureFolderPath;
        private bool _folderInitialized;

        /// <summary>
        /// Get a texture by filename asynchronously.
        /// </summary>
        /// <param name="filename">The texture filename (e.g. "cells/1.png", "clan/4.png").</param>
        /// <returns>Loaded Texture2D.</returns>
        public async UniTask<Texture2D?> GetTextureAsync(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentException("Texture filename cannot be null or empty.", nameof(filename));
            }

            // Return cached texture if available
            if (_textureCache.TryGetValue(filename, out var cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            // Try to load from disk
            var rawData = await LoadTextureFromStorage(filename);

            if (rawData == null)
            {
                throw new FileNotFoundException(
                    $"Required texture '{filename}' was not found in texture storage.",
                    filename);
            }

            if (rawData.Length == 0)
            {
                throw new InvalidDataException($"Texture '{filename}' is empty.");
            }

            var texture = new Texture2D(2, 2);
            bool cacheOwnsTexture = false;
            try
            {
                if (!texture.LoadImage(
                    rawData,
                    markNonReadable: SystemInfo.copyTextureSupport != CopyTextureSupport.None))
                {
                    throw new InvalidDataException($"Texture '{filename}' could not be decoded.");
                }

                texture.name = filename;
                Texture2D storedTexture = _textureCache.GetOrAdd(filename, texture);
                cacheOwnsTexture = ReferenceEquals(storedTexture, texture);
                return storedTexture;
            }
            finally
            {
                if (!cacheOwnsTexture)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>
        /// Get raw texture bytes asynchronously by filename.
        /// </summary>
        /// <param name="filename">The texture filename.</param>
        /// <returns>PNG/WEBP bytes, or null if not found.</returns>
        public async UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default)
        {
            var data = await LoadTextureFromStorage(filename, cancellationToken);
            if (data != null)
            {
                OnTextureLoaded?.Invoke(filename);
            }

            return data;
        }

        public event Action<string>? OnTextureLoaded;

        /// <summary>
        /// Load texture file bytes from storage asynchronously.
        /// Searches persistentDataPath first (dynamic downloads), then bundled Assets/Textures (read-only).
        /// </summary>
        private async UniTask<byte[]?> LoadTextureFromStorage(
            string filename,
            CancellationToken cancellationToken = default)
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            if (!_resolvedPathsCache.TryGetValue(filename, out var fullPath))
            {
                fullPath = ResolveTextureFullPath(filename);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    _resolvedPathsCache.TryAdd(filename, fullPath);
                }
            }

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                if (_enableDebugLogging)
                {
                    Debug.LogWarning($"[TextureStorageManager] File not found for: {filename}");
                }

                return null;
            }

            using var fileStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                true);

            if (fileStream.Length > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Texture '{filename}' exceeds the supported size.");
            }

            var buffer = new byte[(int)fileStream.Length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int bytesRead = await fileStream.ReadAsync(
                    buffer,
                    offset,
                    buffer.Length - offset,
                    cancellationToken);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException(
                        $"Texture '{filename}' ended after {offset} of {buffer.Length} bytes.");
                }

                offset += bytesRead;
            }

            return buffer;
        }

        private string? ResolveTextureFullPath(string filename)
        {
            var normalizedFilename = filename.TrimStart('/');

            // 1. Search persistentDataPath/Textures (dynamic downloads)
            if (!string.IsNullOrEmpty(_textureFolderPath) && Directory.Exists(_textureFolderPath))
            {
                var foundPath = SearchInFolder(_textureFolderPath, normalizedFilename);
                if (foundPath != null)
                {
                    return foundPath;
                }
            }

            // 2. Read-only search in bundled Assets/Textures
            var bundledFolder = Path.Combine(Application.dataPath, "Textures");
            if (Directory.Exists(bundledFolder))
            {
                var foundPath = SearchInFolder(bundledFolder, normalizedFilename);
                if (foundPath != null)
                {
                    return foundPath;
                }
            }

            return null;
        }

        private static string? SearchInFolder(string baseFolder, string filename)
        {
            // Walk each path segment case-insensitively so that
            // "skin/bee.png" finds "Assets/Textures/Skin/bee.png" on macOS
            // (ClientAssetLoader normalises filenames to lowercase).
            var segments = filename.Replace('\\', '/').Split('/');
            string currentDir = baseFolder;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                string seg = segments[i];
                string exactSub = Path.Combine(currentDir, seg);
                if (Directory.Exists(exactSub))
                {
                    currentDir = exactSub;
                }
                else if (Directory.Exists(currentDir))
                {
                    // case-insensitive fallback for macOS
                    var sub = Directory.GetDirectories(currentDir)
                        .FirstOrDefault(d => string.Equals(
                            Path.GetFileName(d), seg,
                            StringComparison.OrdinalIgnoreCase));
                    if (sub == null)
                    {
                        return null;
                    }

                    currentDir = sub;
                }
                else
                {
                    return null;
                }
            }

            string leafName = segments[segments.Length - 1];
            string leafNameWithoutExt = Path.GetFileNameWithoutExtension(leafName);

            // Exact match first
            string leafExact = Path.Combine(currentDir, leafName);
            if (File.Exists(leafExact))
            {
                return leafExact;
            }

            // Wildcard match (any extension), prefer webp -> gif -> png
            if (Directory.Exists(currentDir))
            {
                var files = Directory.GetFiles(currentDir, leafNameWithoutExt + ".*")
                    .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                             && !f.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f =>
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext switch { ".webp" => 0, ".gif" => 1, ".png" => 2, _ => 3 };
                    }).ToArray();

                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Initialize the texture folder path for dynamic runtime downloads.
        /// </summary>
        private void InitializeTextureFolderPath()
        {
            if (_folderInitialized)
            {
                return;
            }

            string persistentDataPath = Application.persistentDataPath;
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new InvalidOperationException(
                    "Application.persistentDataPath is required for texture storage.");
            }

            string persistentPath = Path.Combine(persistentDataPath, "Textures");
            Directory.CreateDirectory(persistentPath);
            _textureFolderPath = persistentPath;
            if (_enableDebugLogging)
            {
                Debug.Log($"[TextureStorageManager] Initialized texture folder: {persistentPath}");
            }

            _folderInitialized = true;
        }

        /// <summary>
        /// Clear the texture cache.
        /// </summary>
        public void ClearCache()
        {
            var textures = new HashSet<Texture2D>();
            foreach (var texture in _textureCache.Values)
            {
                if (texture != null)
                {
                    textures.Add(texture);
                }
            }

            _textureCache.Clear();
            _resolvedPathsCache.Clear();
            foreach (var texture in textures)
            {
                UnityEngine.Object.Destroy(texture);
            }

            if (_enableDebugLogging)
            {
                Debug.Log("[TextureStorageManager] Cache cleared");
            }
        }

        protected void OnDestroy()
        {
            ClearCache();
        }

        public string? GetTextureFolderPath()
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            return _textureFolderPath;
        }

        public bool HasTexture(string filename)
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            var path = ResolveTextureFullPath(filename);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public string GetCacheStats()
        {
            return $"Texture Cache: {_textureCache.Count} entries, Folder: {_textureFolderPath}";
        }
    }
}
