#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.World;
using Fodinae.World.Terrain;
using UnityEngine;

namespace Fodinae
{
    /// <summary>
    /// Thread-safe RAM cache for server assets.
    /// Stores raw bytes + lazily-decoded derived formats (Texture2D, AudioClip, Sprite[]).
    /// Deduplicates concurrent in-flight requests: N callers asking for the same file
    /// share one network round-trip and one format conversion.
    ///
    /// This is the "local CDN" — assets are loaded once from the server, then served
    /// from RAM in any requested format until the application quits.
    /// </summary>
    public sealed class AssetCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _entrySizes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _accessOrder = new();
        private readonly ConcurrentDictionary<string, long> _decodedEntrySizes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _decodedAccessOrder = new();
        private readonly Func<string, CancellationToken, int, UniTask<byte[]?>> _bytesLoader;
        private long _totalBytes;
        private long _maxBytes = ProjectRuntimeContracts.AssetStreaming.AssetCacheCapacityBytes;
        private long _maxDecodedBytes = ProjectRuntimeContracts.AssetStreaming.DecodedAssetCacheCapacityBytes;
        private long _totalDecodedBytes;
        private int _unloadUnusedAssetsRequested;

        // Wall clock rather than Time.unscaledTime: cache maintenance runs from
        // asset-decode continuations that are not guaranteed to be on the main
        // thread, and Unity's time API is.
        private static readonly System.Diagnostics.Stopwatch UnusedAssetsClock =
            System.Diagnostics.Stopwatch.StartNew();
        private const double MinimumSecondsBetweenUnusedAssetCollections = 30.0;
        private double _nextUnusedAssetsCollectionSeconds;

        public AssetCache(Func<string, CancellationToken, int, UniTask<byte[]?>> bytesLoader)
        {
            _bytesLoader = bytesLoader ?? throw new ArgumentNullException(nameof(bytesLoader));
        }

        /// <summary>Retrieve raw bytes. Cached and deduplicated.</summary>
        public UniTask<byte[]?> GetBytesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new CacheEntry(name, this));
            return entry.GetBytesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded Texture2D. Cached after first decode.</summary>
        public UniTask<Texture2D?> GetTextureAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new CacheEntry(name, this));
            return entry.GetTextureAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded AudioClip from WAV bytes. Cached after first decode.</summary>
        public UniTask<AudioClip?> GetAudioAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new CacheEntry(name, this));
            return entry.GetAudioAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve an animated Sprite[] from GIF/WebP. Cached after first decode.</summary>
        public UniTask<Sprite[]?> GetSpritesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new CacheEntry(name, this));
            return entry.GetSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>
        /// Retrieve animated sprites WITH metadata (FPS, frame height).
        /// Use this when you need accurate animation timing from the source file.
        /// </summary>
        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new CacheEntry(name, this));
            return entry.GetAnimatedSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Remove a specific entry from the cache (e.g. on world reset).</summary>
        public void Evict(string filename)
        {
            if (!_entries.TryRemove(filename, out var entry))
            {
                return;
            }

            entry.ReleaseAllReferences();
            RemoveTrackedSize(filename);
            if (_decodedEntrySizes.TryRemove(filename, out var decodedSize))
            {
                Interlocked.Add(ref _totalDecodedBytes, -decodedSize);
            }

            RebuildAccessOrder();

            // No collection here. Evicting one entry is never a reason to walk
            // the whole heap; the interval-gated request from
            // TrimDecodedIfNeeded covers the memory this frees.
        }

        /// <summary>Clear all cached entries.</summary>
        public void Clear()
        {
            foreach (var entry in _entries.Values.ToArray())
            {
                entry.ReleaseAllReferences();
            }

            _entries.Clear();
            _entrySizes.Clear();
            _decodedEntrySizes.Clear();
            while (_accessOrder.TryDequeue(out _))
            {
                // drain access order queue
            }

            while (_decodedAccessOrder.TryDequeue(out _))
            {
                // drain decoded access order queue
            }

            Interlocked.Exchange(ref _totalBytes, 0);
            Interlocked.Exchange(ref _totalDecodedBytes, 0);

            // Clear is a deliberate teardown - a world unload, not maintenance -
            // and it just dropped every reference the cache held, so this is the
            // one place the scan is worth its cost immediately.
            RequestUnusedAssetsCollection(force: true);
        }

        /// <summary>Set the maximum cache size in bytes. Default is 256 MB.</summary>
        public void SetMaxSize(long maxBytes)
        {
            _maxBytes = maxBytes;
            EvictIfNeeded();
        }

        public void SetMaxDecodedSize(long maxBytes)
        {
            _maxDecodedBytes = Math.Max(0, maxBytes);
            TrimDecodedIfNeeded();
        }

        internal void TrackAccess(string filename, long byteSize)
        {
            // An entry is loaded once, but a failed eviction/reload or a concurrent
            // completion can call this more than once. Count each key only once.
            if (_entrySizes.TryAdd(filename, byteSize))
            {
                _accessOrder.Enqueue(filename);
                Interlocked.Add(ref _totalBytes, byteSize);
            }

            EvictIfNeeded();
        }

        internal void TrackDecoded(string filename, long decodedSize)
        {
            if (decodedSize <= 0)
            {
                return;
            }

            if (_decodedEntrySizes.TryAdd(filename, decodedSize))
            {
                _decodedAccessOrder.Enqueue(filename);
                Interlocked.Add(ref _totalDecodedBytes, decodedSize);
            }

            TrimDecodedIfNeeded();
        }

        private void TrimDecodedIfNeeded()
        {
            bool trimmed = false;
            while (Interlocked.Read(ref _totalDecodedBytes) > _maxDecodedBytes &&
                   _decodedAccessOrder.TryDequeue(out var oldest))
            {
                if (!_decodedEntrySizes.TryRemove(oldest, out var size))
                {
                    continue;
                }

                if (_entries.TryGetValue(oldest, out var entry))
                {
                    // Do not Destroy here: active sprites/renderers may still
                    // reference the decoded Unity object. Dropping the cache
                    // reference lets Unity reclaim it once those users vanish.
                    entry.ReleaseDecodedReference();
                }

                Interlocked.Add(ref _totalDecodedBytes, -size);
                trimmed = true;
            }

            if (trimmed)
            {
                RequestUnusedAssetsCollection();
            }
        }

        /// <summary>
        /// Asks Unity to reclaim assets nothing references any more, at most
        /// once every <see cref="MinimumSecondsBetweenUnusedAssetCollections"/>.
        /// </summary>
        /// <remarks>
        /// Resources.UnloadUnusedAssets is the profiler's
        /// GarbageCollectSharedAssets: it walks every managed object and every
        /// Unity object alive in the process to decide what is still
        /// referenced. It is one of the most expensive things a running game
        /// can do, and it is not a cache-maintenance operation.
        ///
        /// The only guard here used to be _unloadUnusedAssetsRequested, which
        /// prevents two scans overlapping and nothing else: the flag clears the
        /// instant a scan completes, so the next trim - or the next single
        /// entry eviction, which used to call this too - started another one
        /// immediately. Under steady eviction pressure, which is the normal
        /// state while textures stream in, that is a continuous back-to-back
        /// loop of full-heap liveness scans, and it looks exactly like a
        /// profile dominated by GC_mark_from and liveness scanning while the
        /// PlayerLoop itself is idle.
        ///
        /// The interval, not the flag, is what makes this safe. Trimming
        /// already dropped the cache's own references; those objects stay
        /// reclaimable until the next scan gets to them, so deferring costs
        /// memory headroom for a while and nothing else.
        /// </remarks>
        /// <param name="force">
        /// Bypasses the interval, for the deliberate teardown in
        /// <see cref="Clear"/>. Never pass true from a maintenance path.
        /// </param>
        private void RequestUnusedAssetsCollection(bool force = false)
        {
            double now = UnusedAssetsClock.Elapsed.TotalSeconds;
            if (!force && now < _nextUnusedAssetsCollectionSeconds)
            {
                return;
            }

            if (Interlocked.Exchange(ref _unloadUnusedAssetsRequested, 1) != 0)
            {
                return;
            }

            _nextUnusedAssetsCollectionSeconds =
                now + MinimumSecondsBetweenUnusedAssetCollections;

            Resources.UnloadUnusedAssets().completed += _ =>
            {
                Interlocked.Exchange(ref _unloadUnusedAssetsRequested, 0);
            };
        }

        private void RemoveTrackedSize(string filename)
        {
            if (_entrySizes.TryRemove(filename, out var size))
            {
                Interlocked.Add(ref _totalBytes, -size);
            }
        }

        private void RebuildAccessOrder()
        {
            while (_accessOrder.TryDequeue(out _))
            {
                // Remove stale keys left by an explicit eviction.
            }

            foreach (var filename in _entrySizes.Keys)
            {
                _accessOrder.Enqueue(filename);
            }
        }

        private void EvictIfNeeded()
        {
            while (Interlocked.Read(ref _totalBytes) > _maxBytes && _accessOrder.Count > 0)
            {
                if (!_accessOrder.TryDequeue(out var oldest))
                {
                    break;
                }

                if (_entries.TryGetValue(oldest, out var entry))
                {
                    // The raw-byte budget must not evict decoded Unity objects.
                    // Renderers and UI may still hold those objects even after
                    // the cache stops retaining the source payload.
                    entry.ReleaseRawBytes();
                }
                else
                {
                    RemoveTrackedSize(oldest);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Internal entry — one per unique filename
        // ──────────────────────────────────────────────────────────────

        private sealed class CacheEntry
        {
            private readonly object _lock = new();
            private readonly string _filename;
            private readonly AssetCache _cache;

            // ── Raw bytes ──
            private byte[]? _bytes;
            private TaskCompletionSource<byte[]?>? _bytesPromise;

            // ── Derivated formats (lazy, computed on first request) ──
            private Texture2D? _texture;
            private TaskCompletionSource<Texture2D?>? _texturePromise;

            private AudioClip? _audio;
            private TaskCompletionSource<AudioClip?>? _audioPromise;
            private bool _wavWarningLogged;

            private Sprite[]? _sprites;
            private TaskCompletionSource<Sprite[]?>? _spritePromise;

            // Stored alongside sprites for AnimatedSpriteData lookups
            private float _spriteFps;
            private int _spriteFrameHeight;
            private int _spriteFrameCount;

            internal CacheEntry(string filename, AssetCache cache)
            {
                _filename = filename;
                _cache = cache;
            }

            internal void ReleaseAllReferences()
            {
                lock (_lock)
                {
                    _texture = null;
                    _sprites = null;
                    _audio = null;
                    _spriteFps = 0f;
                    _spriteFrameHeight = 0;
                    _spriteFrameCount = 0;
                    _bytes = null;
                }
            }

            internal void ReleaseDecodedReference()
            {
                lock (_lock)
                {
                    _texture = null;
                    _sprites = null;
                    _audio = null;
                    _spriteFps = 0f;
                    _spriteFrameHeight = 0;
                    _spriteFrameCount = 0;
                }
            }

            internal long EstimateDecodedBytes()
            {
                lock (_lock)
                {
                    var textures = new HashSet<Texture2D>();
                    if (_texture != null)
                    {
                        textures.Add(_texture);
                    }

                    if (_sprites != null)
                    {
                        for (int i = 0; i < _sprites.Length; i++)
                        {
                            if (_sprites[i] != null && _sprites[i].texture != null)
                            {
                                textures.Add(_sprites[i].texture);
                            }
                        }
                    }

                    long total = 0;
                    foreach (var texture in textures)
                    {
                        total += (long)texture.width * texture.height * 4;
                    }

                    return total;
                }
            }

            // ── Public API ──

            public UniTask<byte[]?> GetBytesAsync(Func<UniTask<byte[]?>> loader)
            {
                // Fast path — already loaded
                lock (_lock)
                {
                    if (_bytes != null)
                    {
                        return UniTask.FromResult<byte[]?>(_bytes);
                    }

                    if (_bytesPromise != null)
                    {
                        return AwaitTask(_bytesPromise.Task);
                    }
                }

                // First caller — create the promise
                TaskCompletionSource<byte[]?> promise;
                lock (_lock)
                {
                    if (_bytes != null)
                    {
                        return UniTask.FromResult<byte[]?>(_bytes);
                    }

                    if (_bytesPromise != null)
                    {
                        return AwaitTask(_bytesPromise.Task);
                    }

                    _bytesPromise = promise = new TaskCompletionSource<byte[]?>();
                }

                return LoadBytes(promise, loader);
            }

            public UniTask<Texture2D?> GetTextureAsync(Func<UniTask<byte[]?>> loader)
            {
                lock (_lock)
                {
                    if (_texture != null)
                    {
                        return UniTask.FromResult<Texture2D?>(_texture);
                    }

                    if (_texturePromise != null)
                    {
                        return AwaitTask(_texturePromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_texture != null)
                    {
                        return UniTask.FromResult<Texture2D?>(_texture);
                    }

                    if (_texturePromise != null)
                    {
                        return AwaitTask(_texturePromise.Task);
                    }

                    _texturePromise = new TaskCompletionSource<Texture2D?>();
                }

                return DecodeTexture(loader);
            }

            public UniTask<AudioClip?> GetAudioAsync(Func<UniTask<byte[]?>> loader)
            {
                lock (_lock)
                {
                    if (_audio != null)
                    {
                        return UniTask.FromResult<AudioClip?>(_audio);
                    }

                    if (_audioPromise != null)
                    {
                        return AwaitTask(_audioPromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_audio != null)
                    {
                        return UniTask.FromResult<AudioClip?>(_audio);
                    }

                    if (_audioPromise != null)
                    {
                        return AwaitTask(_audioPromise.Task);
                    }

                    _audioPromise = new TaskCompletionSource<AudioClip?>();
                }

                return DecodeAudio(loader);
            }

            public UniTask<Sprite[]?> GetSpritesAsync(Func<UniTask<byte[]?>> loader)
            {
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult<Sprite[]?>(_sprites);
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitTask(_spritePromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult<Sprite[]?>(_sprites);
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitTask(_spritePromise.Task);
                    }

                    _spritePromise = new TaskCompletionSource<Sprite[]?>();
                }

                return DecodeSprites(loader);
            }

            public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(Func<UniTask<byte[]?>> loader)
            {
                // Fast path — already decoded
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(new AnimatedSpriteData(_sprites, _spriteFps, _spriteFrameHeight));
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitAnimatedSprites(_spritePromise.Task);
                    }
                }

                // New request — the first ResolveSprites call populates the promise
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(new AnimatedSpriteData(_sprites, _spriteFps, _spriteFrameHeight));
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitAnimatedSprites(_spritePromise.Task);
                    }

                    _spritePromise = new TaskCompletionSource<Sprite[]?>();
                }

                return DecodeAndWrapSprites(loader);
            }

            // ── Private Static Methods ──

            private async UniTask<AnimatedSpriteData> AwaitAnimatedSprites(Task<Sprite[]?> task)
            {
                var frames = await task;
                if (frames == null)
                {
                    throw new InvalidOperationException("Sprite frames were not decoded (null).");
                }

                lock (_lock)
                {
                    return new AnimatedSpriteData(
                        frames,
                        _spriteFps,
                        _spriteFrameHeight);
                }
            }

            private static async UniTask<T> AwaitTask<T>(Task<T> task)
            {
                return await task;
            }

            // ── Private Instance Methods ──

            private async UniTask<byte[]?> LoadBytes(TaskCompletionSource<byte[]?> promise, Func<UniTask<byte[]?>> loader)
            {
                try
                {
                    var bytes = await loader();
                    lock (_lock)
                    {
                        _bytes = bytes;
                        _bytesPromise = null;
                    }

                    if (bytes != null && bytes.Length > 0)
                    {
                        _cache.TrackAccess(_filename, bytes.Length);
                    }

                    promise.TrySetResult(bytes);
                    return bytes;
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _bytesPromise = null;
                    }

                    promise.TrySetException(ex);
                    throw;
                }
            }

            private async UniTask<Texture2D?> DecodeTexture(Func<UniTask<byte[]?>> loader)
            {
                try
                {
                    // First ensure bytes are loaded
                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        var emptyEx = new InvalidOperationException($"Empty or null bytes for texture '{_filename}'.");
                        FailTexture(emptyEx);
                        throw emptyEx;
                    }

                    // Decode on the main thread (Unity API requirement)
                    await UniTask.SwitchToMainThread();

                    var containerType = AnimationContainerDecoder.DetectType(bytes);
                    Texture2D? result;
                    float animationFps = 0f;
                    int animationFrameHeight = 0;
                    int animationFrameCount = 0;

                    if (containerType == AnimationContainerDecoder.ContainerType.GIF)
                    {
                        var decoded = AnimationContainerDecoder.DecodeGif(bytes);
                        result = decoded.Atlas;
                        animationFps = decoded.FPS;
                        animationFrameHeight = decoded.FrameHeight;
                        animationFrameCount = decoded.FrameCount;
                        if (result != null)
                        {
                            result.name = $"Cache_GIF_{DateTime.Now.Ticks}";
                            RuntimeTextureFactory.ApplySampling(
                                result,
                                FilterMode.Point,
                                TextureWrapMode.Clamp);
                        }
                    }
                    else if (containerType == AnimationContainerDecoder.ContainerType.WebP)
                    {
                        var decoded = AnimationContainerDecoder.DecodeWebP(bytes);
                        result = decoded.Atlas;
                        animationFps = decoded.FPS;
                        animationFrameHeight = decoded.FrameHeight;
                        animationFrameCount = decoded.FrameCount;
                        if (result != null)
                        {
                            result.name = $"Cache_WebP_{DateTime.Now.Ticks}";
                            RuntimeTextureFactory.ApplySampling(
                                result,
                                FilterMode.Point,
                                TextureWrapMode.Clamp);
                        }
                    }
                    else
                    {
                        // ImageConversion.LoadImage changes PNGs to ARGB32.
                        // Canonicalize every decoded image to RGBA32 so Metal
                        // can copy it into the RGBA32 terrain atlas without
                        // relying on API-specific format compatibility.
                        bool makeNoLongerReadable =
                            RuntimeTextureFactory.SupportsTexture2DGpuCopy;
                        result = RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                            bytes,
                            $"Cache_Tex_{DateTime.Now.Ticks}",
                            RuntimeTextureColorSpace.Srgb,
                            FilterMode.Point,
                            TextureWrapMode.Clamp,
                            makeNoLongerReadable: makeNoLongerReadable);
                    }

                    TaskCompletionSource<Texture2D?>? texPromise;
                    lock (_lock)
                    {
                        _texture = result;
                        _spriteFps = animationFps;
                        _spriteFrameHeight = animationFrameHeight;
                        _spriteFrameCount = animationFrameCount;
                        texPromise = _texturePromise;
                        _texturePromise = null;
                    }

                    _cache.TrackDecoded(_filename, EstimateDecodedBytes());
                    texPromise?.TrySetResult(result);
                    ReleaseRawBytes();
                    return result;
                }
                catch (Exception ex)
                {
                    FailTexture(ex);
                    throw;
                }
            }

            private void FailTexture(Exception ex)
            {
                ReleaseRawBytes();
                lock (_lock)
                {
                    _texturePromise?.TrySetException(ex);
                    _texturePromise = null;
                }
            }

            private async UniTask<AudioClip?> DecodeAudio(Func<UniTask<byte[]?>> loader)
            {
                try
                {
                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        var emptyEx = new InvalidOperationException($"Empty or null bytes for audio '{_filename}'.");
                        FailAudio(emptyEx);
                        throw emptyEx;
                    }

                    if (!_wavWarningLogged)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[AssetCache] WAV decoding is unsupported for '{_filename}'; request will fail.");
                        _wavWarningLogged = true;
                    }
                    AudioClip? clip = null;
                    TaskCompletionSource<AudioClip?>? audioPromise;
                    lock (_lock)
                    {
                        _audio = clip;
                        audioPromise = _audioPromise;
                        _audioPromise = null;
                    }

                    var unsupportedEx = new NotSupportedException($"WAV decoding is not supported for '{_filename}'.");
                    audioPromise?.TrySetException(unsupportedEx);
                    ReleaseRawBytes();
                    throw unsupportedEx;
                }
                catch (Exception ex)
                {
                    FailAudio(ex);
                    throw;
                }
            }

            private void FailAudio(Exception ex)
            {
                ReleaseRawBytes();
                lock (_lock)
                {
                    _audioPromise?.TrySetException(ex);
                    _audioPromise = null;
                }
            }

            private async UniTask<AnimatedSpriteData> DecodeAndWrapSprites(Func<UniTask<byte[]?>> loader)
            {
                var frames = await DecodeSprites(loader);
                lock (_lock)
                {
                    if (frames == null)
                    {
                        throw new InvalidOperationException("Sprite frames were not decoded (null).");
                    }

                    return new AnimatedSpriteData(frames, _spriteFps, _spriteFrameHeight);
                }
            }

            private async UniTask<Sprite[]?> DecodeSprites(Func<UniTask<byte[]?>> loader)
            {
                try
                {
                    Texture2D? cachedAnimationTexture;
                    float cachedFps;
                    int cachedFrameHeight;
                    int cachedFrameCount;
                    TaskCompletionSource<Sprite[]?>? cachedSpritePromise;
                    lock (_lock)
                    {
                        cachedAnimationTexture = _texture;
                        cachedFps = _spriteFps;
                        cachedFrameHeight = _spriteFrameHeight;
                        cachedFrameCount = _spriteFrameCount;
                        cachedSpritePromise = _spritePromise;
                    }

                    // Texture and animated-sprite requests share the same atlas.
                    // Decoding the same GIF/WebP twice was a large native-memory
                    // spike and left duplicate GPU textures alive.
                    if (cachedAnimationTexture != null && cachedFrameHeight > 0)
                    {
                        int frameCount = cachedFrameCount > 0
                            ? cachedFrameCount
                            : Mathf.Max(1, cachedAnimationTexture.height / cachedFrameHeight);
                        Sprite[] cachedSprites = AnimationContainerDecoder.Decode(
                            cachedAnimationTexture,
                            cachedAnimationTexture.width,
                            cachedFrameHeight,
                            frameCount);
                        lock (_lock)
                        {
                            _sprites = cachedSprites;
                            _spriteFps = cachedFps;
                            _spriteFrameCount = frameCount;
                            _spritePromise = null;
                        }

                        _cache.TrackDecoded(_filename, EstimateDecodedBytes());
                        cachedSpritePromise?.TrySetResult(cachedSprites);
                        return cachedSprites;
                    }

                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        var emptyEx = new InvalidOperationException($"Empty or null bytes for sprites '{_filename}'.");
                        FailSprites(emptyEx);
                        throw emptyEx;
                    }

                    // Decode GIF/WebP on the main thread
                    await UniTask.SwitchToMainThread();

                    var containerType = AnimationContainerDecoder.DetectType(bytes);
                    AnimationContainerDecoder.DecodedAnimation anim;

                    if (containerType == AnimationContainerDecoder.ContainerType.GIF)
                    {
                        anim = AnimationContainerDecoder.DecodeGif(bytes);
                    }
                    else if (containerType == AnimationContainerDecoder.ContainerType.WebP)
                    {
                        anim = AnimationContainerDecoder.DecodeWebP(bytes);
                    }
                    else
                    {
                        anim = default;
                    }

                    Sprite[] result;
                    float fps;
                    int frameHeight = 0;

                    if (anim.Atlas != null && anim.FrameCount > 0)
                    {
                        fps = anim.FPS;
                        frameHeight = anim.FrameHeight;
                        anim.Atlas.name = $"Cache_Animation_{DateTime.Now.Ticks}";
                        RuntimeTextureFactory.ApplySampling(
                            anim.Atlas,
                            FilterMode.Point,
                            TextureWrapMode.Clamp);
                        result = AnimationContainerDecoder.Decode(
                            anim.Atlas, anim.Atlas.width, anim.FrameHeight, anim.FrameCount);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unknown or empty animation container for '{_filename}'.");
                    }

                    TaskCompletionSource<Sprite[]?>? spritePromise;
                    lock (_lock)
                    {
                        _sprites = result;
                        _spriteFps = fps;
                        _spriteFrameHeight = frameHeight;
                        _spriteFrameCount = anim.FrameCount;
                        _texture = anim.Atlas;
                        spritePromise = _spritePromise;
                        _spritePromise = null;
                    }

                    _cache.TrackDecoded(_filename, EstimateDecodedBytes());
                    spritePromise?.TrySetResult(result);
                    ReleaseRawBytes();
                    return result;
                }
                catch (Exception ex)
                {
                    FailSprites(ex);
                    throw;
                }
            }

            private void FailSprites(Exception ex)
            {
                ReleaseRawBytes();
                lock (_lock)
                {
                    _spritePromise?.TrySetException(ex);
                    _spritePromise = null;
                }
            }

            internal void ReleaseRawBytes()
            {
                lock (_lock)
                {
                    if (_bytes == null)
                    {
                        return;
                    }

                    _bytes = null;
                }

                _cache.RemoveTrackedSize(_filename);
            }
        }
    }
}
