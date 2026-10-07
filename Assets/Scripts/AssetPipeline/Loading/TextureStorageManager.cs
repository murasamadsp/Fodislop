#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;
using VContainer;

namespace Kern.AssetPipeline
{
    public class TextureStorageManager : MonoBehaviour, ITextureStorageService
    {
        // Декодирование и копия в RGBA32 идут на главном потоке: маркер
        // нужен, чтобы провис кадра на приезде текстур был виден в FrameStall.
        private static readonly Unity.Profiling.ProfilerMarker s_decodeMarker = new("Kern.Textures.Decode");

        private int _lastDecodeFrame = -1;

        [Inject]
        private IRuntimeAssetPaths _runtimeAssetPaths = null!;
        [SerializeField]
        private bool _enableDebugLogging = false;

        private readonly ConcurrentDictionary<string, Texture2D> _textureCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _resolvedPathsCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte[]> _rawBytesCache =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _textureFolderPath;
        private bool _folderInitialized;

        private void StoreInRawBytesCache(string normalizedFilename, byte[] bytes, string? fullPath = null)
        {
            _rawBytesCache[normalizedFilename] = bytes;

            string ext = Path.GetExtension(normalizedFilename);
            if (!string.IsNullOrEmpty(ext))
            {
                string withoutExt = normalizedFilename.Substring(0, normalizedFilename.Length - ext.Length);
                _rawBytesCache.TryAdd(withoutExt, bytes);
            }
            else if (!string.IsNullOrEmpty(fullPath))
            {
                string fullExt = Path.GetExtension(fullPath);
                if (!string.IsNullOrEmpty(fullExt))
                {
                    string withExt = normalizedFilename + fullExt;
                    _rawBytesCache.TryAdd(withExt, bytes);
                }
            }
        }

        private void StoreInTextureCache(string normalizedFilename, Texture2D texture)
        {
            _textureCache[normalizedFilename] = texture;

            string ext = Path.GetExtension(normalizedFilename);
            if (!string.IsNullOrEmpty(ext))
            {
                string withoutExt = normalizedFilename.Substring(0, normalizedFilename.Length - ext.Length);
                _textureCache.TryAdd(withoutExt, texture);
            }
            else if (_resolvedPathsCache.TryGetValue(normalizedFilename, out string? fullPath) && !string.IsNullOrEmpty(fullPath))
            {
                string fullExt = Path.GetExtension(fullPath);
                if (!string.IsNullOrEmpty(fullExt))
                {
                    string withExt = normalizedFilename + fullExt;
                    _textureCache.TryAdd(withExt, texture);
                }
            }
        }

        /// <param name="filename">The texture filename (e.g. "cells/Rock.png", "clan/4.png").</param>
        /// <returns>Loaded Texture2D.</returns>
        public async UniTask<Texture2D?> GetTextureAsync(
            string filename,
            CancellationToken cancellationToken = default)
        {
            string normalizedFilename = NormalizeRelativeTexturePath(filename);

            // Return cached texture if available
            if (_textureCache.TryGetValue(normalizedFilename, out var cachedTexture) &&
                cachedTexture != null)
            {
                return cachedTexture;
            }

            // Try to load from disk
            var rawData = await LoadTextureFromStorage(
                normalizedFilename,
                cancellationToken);

            if (rawData == null)
            {
                throw new FileNotFoundException(
                    $"Required texture '{normalizedFilename}' was not found in texture storage.",
                    normalizedFilename);
            }

            if (rawData.Length == 0)
            {
                throw new InvalidDataException(
                    $"Texture '{normalizedFilename}' is empty.");
            }

            await UniTask.SwitchToMainThread(cancellationToken);

            // Не больше одного декодирования за кадр. Декодирование и копия в
            // RGBA32 стоят миллисекунды на главном потоке, а текстуры
            // приезжают пачками: несколько штук подряд в одном кадре давали
            // фриз. Очередь по кадрам растягивает ту же работу без пиков.
            while (_lastDecodeFrame == Time.frameCount)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            _lastDecodeFrame = Time.frameCount;
            Texture2D texture;
            using (s_decodeMarker.Auto())
            {
                texture = DecodeTexture(normalizedFilename, rawData);
            }

            Kern.Core.Interfaces.Diagnostics.FrameEventLog.Record(
                $"декодирована {normalizedFilename} {texture.width}×{texture.height}");

            bool cacheOwnsTexture = false;
            try
            {
                texture.name = normalizedFilename;
                RuntimeTextureFactory.ApplySampling(
                    texture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                Texture2D storedTexture = _textureCache.GetOrAdd(
                    normalizedFilename,
                    texture);
                cacheOwnsTexture = ReferenceEquals(storedTexture, texture);
                StoreInTextureCache(normalizedFilename, storedTexture);
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

        private static Texture2D DecodeTexture(string filename, byte[] data)
        {
            bool makeNoLongerReadable = RuntimeTextureFactory.SupportsTexture2DGPUCopy;
            return RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                data,
                filename,
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                makeNoLongerReadable: makeNoLongerReadable);
        }

        /// <param name="filename">The texture filename.</param>
        /// <returns>Image bytes, or null if not found.</returns>
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

        private async UniTask<byte[]?> LoadTextureFromStorage(
            string filename,
            CancellationToken cancellationToken = default)
        {
            string normalizedFilename = NormalizeRelativeTexturePath(filename);

            if (_rawBytesCache.TryGetValue(normalizedFilename, out byte[]? cachedBytes))
            {
                return cachedBytes;
            }

            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            if (!_resolvedPathsCache.TryGetValue(normalizedFilename, out var fullPath))
            {
                fullPath = ResolveTextureFullPath(normalizedFilename);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    _resolvedPathsCache.TryAdd(normalizedFilename, fullPath);
                }
            }

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                if (_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[TextureStorageManager] File not found for: {normalizedFilename}");
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
                        $"Texture '{normalizedFilename}' ended after {offset} of {buffer.Length} bytes.");
                }

                offset += bytesRead;
            }

            StoreInRawBytesCache(normalizedFilename, buffer, fullPath);
            return buffer;
        }

        private string? ResolveTextureFullPath(string filename)
        {
            string normalizedFilename = NormalizeRelativeTexturePath(filename);
            return _runtimeAssetPaths?.FindTextureFile(normalizedFilename);
        }

        private static string NormalizeRelativeTexturePath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentException(
                    "Texture filename cannot be null or whitespace.",
                    nameof(filename));
            }

            if (Path.IsPathRooted(filename))
            {
                throw new ArgumentException(
                    $"Texture filename must be relative: '{filename}'.",
                    nameof(filename));
            }

            string normalized = filename.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            if (segments.Length == 0 ||
                segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment == "." ||
                    segment == ".."))
            {
                throw new ArgumentException(
                    $"Texture filename contains an invalid path segment: '{filename}'.",
                    nameof(filename));
            }

            return string.Join("/", segments);
        }

        private void InitializeTextureFolderPath()
        {
            if (_folderInitialized || _runtimeAssetPaths == null)
            {
                return;
            }

            string persistentPath = _runtimeAssetPaths.PersistentTexturesRoot;
            _textureFolderPath = persistentPath;
            if (_enableDebugLogging)
            {
                Debug.Log($"[TextureStorageManager] Initialized texture folder: {persistentPath}");
            }

            _folderInitialized = true;
        }

        public void ClearCache()
        {
            // Loaded textures can still be referenced by renderers and UI when
            // this service is rebuilt after a domain reload. Clearing ownership
            // must not invalidate those live Unity objects.
            _textureCache.Clear();
            _resolvedPathsCache.Clear();
            _rawBytesCache.Clear();

            if (_enableDebugLogging)
            {
                Debug.Log("[TextureStorageManager] Cache cleared");
            }
        }

        protected void OnDestroy()
        {
            ClearCache();
        }

        public bool HasTexture(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            string normalizedFilename;
            try
            {
                normalizedFilename = NormalizeRelativeTexturePath(filename);
            }
            catch
            {
                return false;
            }

            if (_rawBytesCache.ContainsKey(normalizedFilename) || _textureCache.ContainsKey(normalizedFilename))
            {
                return true;
            }

            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            var path = ResolveTextureFullPath(normalizedFilename);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public async UniTask PreloadTexturesAsync(
            IReadOnlyList<string> filenames,
            CancellationToken cancellationToken = default)
        {
            if (filenames == null || filenames.Count == 0)
            {
                return;
            }

            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            var toDecode = new List<(string Name, byte[] Data)>();

            foreach (string filename in filenames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                string normalized;
                try
                {
                    normalized = NormalizeRelativeTexturePath(filename);
                }
                catch
                {
                    continue;
                }

                if (_rawBytesCache.TryGetValue(normalized, out byte[]? cachedBytes))
                {
                    if (!_textureCache.ContainsKey(normalized))
                    {
                        toDecode.Add((normalized, cachedBytes));
                    }

                    continue;
                }

                byte[]? data = await LoadTextureFromStorage(normalized, cancellationToken);
                if (data != null && data.Length > 0 && !_textureCache.ContainsKey(normalized))
                {
                    toDecode.Add((normalized, data));
                }
            }

            if (toDecode.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await UniTask.SwitchToMainThread(cancellationToken);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            const long frameBudgetMs = 4;

            for (int i = 0; i < toDecode.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var (texName, rawBytes) = toDecode[i];
                if (_textureCache.ContainsKey(texName))
                {
                    continue;
                }

                Texture2D texture;
                using (s_decodeMarker.Auto())
                {
                    texture = DecodeTexture(texName, rawBytes);
                }

                texture.name = texName;
                RuntimeTextureFactory.ApplySampling(
                    texture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                StoreInTextureCache(texName, texture);

                if (stopwatch.ElapsedMilliseconds >= frameBudgetMs && !Application.isBatchMode)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    stopwatch.Restart();
                }
            }
        }

        public string GetCacheStats()
        {
            return $"Texture Cache: {_textureCache.Count} textures, {_rawBytesCache.Count} raw entries, Folder: {_textureFolderPath}";
        }
    }
}
