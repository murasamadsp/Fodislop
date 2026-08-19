#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Networking.Connection;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae
{
    using static ETagCalculator;
    using static PersistentAssetCache;

    [DefaultExecutionOrder(-10000)]
    public class ClientAssetLoader : MonoBehaviour, IAssetLoader
    {
        private readonly AssetCache _cache = new(LoadBytesFromServerInternal);

        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
        private readonly ConcurrentDictionary<string, byte> _missingAssets = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _loopCts;
        private bool _packetSubscribed;

        public int PendingAssetCount => _pendingRequests.Count;
        public int QueuedAssetCount => _requestQueue.Count;

        public string[] GetPendingAssetNames()
        {
            return new List<string>(_pendingRequests.Keys).ToArray();
        }

        [Inject]
        private IConnectionService _connectionService = null!;

        private bool _assetSubscriptionEstablished;
        private IConnectionService? _subscribedConnection;

        public bool IsAssetSubscriptionEstablished => _assetSubscriptionEstablished;

        protected void Awake()
        {
            _loopCts = new CancellationTokenSource();
            ProcessBatchLoop(_loopCts.Token).Forget();
        }

        protected void OnDestroy()
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _cache.Clear();
            _missingAssets.Clear();

            foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> pending in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(pending.Key, out TaskCompletionSource<byte[]>? request))
                {
                    request.TrySetException(
                        new ObjectDisposedException(nameof(ClientAssetLoader)));
                }
            }

            UnsubscribeFromConnection();
        }

        /// <summary>
        /// Binds the packet stream after VContainer injection. Unity may call
        /// Awake/OnEnable before [Inject] has populated the connection field,
        /// and OnDestroy may fire during domain reload before any injection.
        /// </summary>
        public void EnsureAssetSubscription()
        {
            if (ServiceLocator.IsInitialized)
            {
                _connectionService = ServiceLocator.Resolve<IConnectionService>() ??
                    throw new InvalidOperationException(
                        "ClientAssetLoader requires IConnectionService in the active resolver.");
            }

            if (_subscribedConnection != null)
            {
                _subscribedConnection.OnPacketReceived -= OnPacketReceived;
                _subscribedConnection = null;
            }

            if (_connectionService == null)
            {
                throw new InvalidOperationException(
                    "ClientAssetLoader requires IConnectionService before subscription.");
            }

            // Rebind after domain reloads: the connection service may be a new
            // instance while this loader and its boolean state survived.
            _connectionService.OnPacketReceived -= OnPacketReceived;
            _connectionService.OnPacketReceived += OnPacketReceived;
            _subscribedConnection = _connectionService;
            _assetSubscriptionEstablished = true;
            _missingAssets.Clear();
        }

        private void UnsubscribeFromConnection()
        {
            if (_subscribedConnection == null)
            {
                _assetSubscriptionEstablished = false;
                return;
            }

            _subscribedConnection.OnPacketReceived -= OnPacketReceived;
            _subscribedConnection = null;
            _assetSubscriptionEstablished = false;
        }

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetRequestTimeoutSeconds)
        {
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (IsAudioBank(cleanFilename) && _missingAssets.ContainsKey(cleanFilename))
            {
                return UniTask.FromResult<byte[]?>(null);
            }

            return _cache.GetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
        }

        public async UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetRequestTimeoutSeconds)
        {
            var cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (IsAudioBank(cleanFilename) && _missingAssets.ContainsKey(cleanFilename))
            {
                throw new FileNotFoundException(
                    $"Optional audio asset '{cleanFilename}' is unavailable.",
                    cleanFilename);
            }

            byte[]? bytes = await GetAssetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
            if (bytes == null || bytes.Length == 0 || !PersistentAssetCache.HasAsset(cleanFilename))
            {
                if (IsAudioBank(cleanFilename))
                {
                    _missingAssets.TryAdd(cleanFilename, 0);
                }

                throw new FileNotFoundException(
                    $"Required asset '{cleanFilename}' could not be loaded or persisted.",
                    cleanFilename);
            }

            return PersistentAssetCache.GetAssetPath(cleanFilename);
        }

        public bool IsKnownMissing(string filename)
        {
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            return _missingAssets.ContainsKey(cleanFilename);
        }

        public async UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            Texture2D? texture = await _cache.GetTextureAsync(filename, cancellationToken);
            return texture ?? throw new FileNotFoundException(
                $"Required texture '{filename}' could not be loaded.",
                filename);
        }

        public UniTask<AudioClip?> GetAudioAsync(string filename, CancellationToken cancellationToken = default)
        {
            return _cache.GetAudioAsync(filename, cancellationToken);
        }

        public UniTask<Sprite[]?> GetSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            return _cache.GetSpritesAsync(filename, cancellationToken);
        }

        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            return _cache.GetAnimatedSpritesAsync(filename, cancellationToken);
        }

        public async UniTaskVoid LoadAndApplyTexture(Action<Texture2D> applyTextureAction, string filename, CancellationToken cancellationToken)
        {
            Texture2D? texture = await GetTextureAsync(filename, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (texture == null)
            {
                throw new FileNotFoundException(
                    $"Required texture '{filename}' could not be applied.",
                    filename);
            }

            applyTextureAction(texture);
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public void EnsurePacketSubscription()
        {
            if (_packetSubscribed)
            {
                return;
            }

            if (_connectionService is ConnectionManager cm)
            {
                cm.OnPacketReceived -= OnPacketReceived;
                cm.OnPacketReceived += OnPacketReceived;
                _packetSubscribed = true;
            }
        }

        private static async UniTask<byte[]?> LoadBytesFromServerInternal(string filename, CancellationToken ct, int timeoutSeconds)
        {
            var instance = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader ??
                throw new InvalidOperationException(
                    "ClientAssetLoader is not registered in the active container.");

            return await instance.LoadBytesFromServer(filename, ct, timeoutSeconds);
        }

        private async UniTask<byte[]?> LoadBytesFromServer(string filename, CancellationToken ct, int timeoutSeconds)
        {
            filename = filename.TrimStart('/').ToLowerInvariant();

            // 1. Check local RAM/disk cache first when offline
            var connectionService = ServiceLocator.Resolve<IConnectionService>()!;
            var isConnected = connectionService.IsConnected;

            if (!isConnected)
            {
                if (HasAsset(filename))
                {
                    return await GetAssetAsync(filename);
                }
            }

            // 2. Check local TextureStorageManager if available
            if (IsTextureFile(filename))
            {
                var tsm = ServiceLocator.Resolve<ITextureStorageService>();
                bool tsmHas = tsm != null && tsm.HasTexture(filename);
                if (tsmHas && tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await SaveAssetAsync(filename, localData, string.Empty);
                        return localData;
                    }
                }
            }

            // 3. Try server network request if connected
            if (isConnected)
            {
                string? etag = HasAsset(filename) ? await GetETagAsync(filename) : null;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var result = await GetAssetBytesFromServer(filename, etag ?? string.Empty, cts.Token);
                    if (result != null && result.Length > 0)
                    {
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancellation is expected when requests are superseded
                }
                catch (Exception ex)
                {
                    if (IsAudioBank(filename))
                    {
                        Debug.Log(
                            $"[ClientAssetLoader] Optional audio asset '{filename}' unavailable; skipping.");
                    }
                    else
                    {
                        Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
                    }
                }
            }

            // 4. Fallback to cached asset
            if (HasAsset(filename))
            {
                return await GetAssetAsync(filename);
            }

            if (IsTextureFile(filename))
            {
                var tsm = ServiceLocator.Resolve<ITextureStorageService>();
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await SaveAssetAsync(filename, localData, string.Empty);
                        return localData;
                    }
                }
            }

            if (IsAudioBank(filename))
            {
                _missingAssets.TryAdd(filename, 0);
            }

            return null;
        }

        private static bool IsTextureFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            if (filename.EndsWith(".webp.bytes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return string.IsNullOrEmpty(ext) || ext == ".png" || ext == ".jpg" ||
                ext == ".jpeg" || ext == ".webp" || ext == ".gif" ||
                ext == ".exr";
        }

        private static bool IsAudioBank(string filename)
        {
            return string.Equals(
                Path.GetExtension(filename),
                ".bank",
                StringComparison.OrdinalIgnoreCase);
        }

        private async UniTaskVoid ProcessBatchLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(50, cancellationToken: ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_requestQueue.IsEmpty)
                {
                    continue;
                }

                List<RuntimeAssetEntryPacket> batch = new();
                while (_requestQueue.TryDequeue(out var entry))
                {
                    if (_pendingRequests.TryGetValue(entry.Filename, out var tcs) && !tcs.Task.IsCompleted)
                    {
                        if (!batch.Exists(x => x.Filename == entry.Filename))
                        {
                            batch.Add(entry);
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    var connectionService = ServiceLocator.Resolve<IConnectionService>()!;
                    if (connectionService.IsConnected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        connectionService.Send(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
                    }
                    else
                    {
                        foreach (var entry in batch)
                        {
                            if (_pendingRequests.TryRemove(entry.Filename, out var tcs))
                            {
                                tcs.TrySetException(new Exception("Connection lost while sending asset request batch"));
                            }
                        }
                    }
                }
            }
        }

        private async void OnPacketReceived(ServerPacket obj)
        {
            if (obj.Payload is not RuntimeAssetPacket assetPacket)
            {
                return;
            }

            string filename;
            try
            {
                filename = string.IsNullOrWhiteSpace(assetPacket.Filename)
                    ? throw new InvalidDataException("Server returned an asset packet without a filename.")
                    : assetPacket.Filename.TrimStart('/').ToLowerInvariant();
            }
            catch (Exception exception)
            {
                _connectionService.TriggerDisconnect(
                    $"Invalid runtime asset packet: {exception.Message}");
                return;
            }

            if (!_pendingRequests.TryRemove(filename, out var tcs))
            {
                return;
            }

            try
            {
                byte[]? contents = assetPacket.Contents;

                // A conditional asset response may omit the body entirely or
                // serialize it as an empty array. Both forms mean "use the
                // cached representation" when the server supplied an ETag.
                if ((contents == null || contents.Length == 0) &&
                    !string.IsNullOrEmpty(assetPacket.ETag))
                {
                    byte[]? cachedAsset = await GetAssetAsync(assetPacket.Filename).ConfigureAwait(false);
                    if (cachedAsset == null || cachedAsset.Length == 0)
                    {
                        throw new InvalidDataException(
                            $"Asset '{filename}' is not cached and server returned empty contents.");
                    }

                    tcs.TrySetResult(cachedAsset);
                    return;
                }

                if (contents == null || contents.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Server returned empty asset contents for '{filename}' without a usable ETag/cache entry.");
                }

                string etag = Calculate(contents) ??
                    throw new InvalidDataException(
                        $"Asset '{filename}' produced no ETag after download.");
                await SaveAssetAsync(assetPacket.Filename, contents, etag).ConfigureAwait(false);
                _missingAssets.TryRemove(assetPacket.Filename, out _);
                tcs.TrySetResult(contents);
            }
            catch (Exception exception)
            {
                tcs.TrySetException(exception);

                // A missing optional asset is a request-scoped failure. It must
                // propagate to the caller so required assets can fail fast, but
                // it must not disconnect an otherwise healthy game session.
                // Optional callers deliberately catch this and continue without
                // their visual decoration.
            }
        }

        private async UniTask<byte[]> GetAssetBytesFromServer(string filename, string etag, CancellationToken cancellationToken)
        {
            bool isNew = false;
            var tcs = _pendingRequests.GetOrAdd(filename, _ =>
            {
                isNew = true;
                return new TaskCompletionSource<byte[]>();
            });

            if (!isNew)
            {
                return await tcs.Task;
            }

            using var registration = cancellationToken.Register(() =>
            {
                tcs.TrySetCanceled();
                _pendingRequests.TryRemove(filename, out _);
            });

            var connectionService = ServiceLocator.Resolve<IConnectionService>()!;
            if (!connectionService.IsConnected)
            {
                try
                {
                    var tsm = ServiceLocator.Resolve<ITextureStorageService>();
                    if (tsm != null)
                    {
                        var localData = await tsm.GetTextureData(filename);
                        if (localData != null)
                        {
                            tcs.TrySetResult(localData);
                            _pendingRequests.TryRemove(filename, out _);
                            return localData;
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    _pendingRequests.TryRemove(filename, out _);
                    throw;
                }

                var noConnEx = new Exception($"No active connection and no local resource found for {filename}");
                tcs.TrySetException(noConnEx);
                _pendingRequests.TryRemove(filename, out _);
                throw noConnEx;
            }

            _requestQueue.Enqueue(new RuntimeAssetEntryPacket(filename, etag ?? string.Empty));

            try
            {
                return await tcs.Task;
            }
            catch
            {
                _pendingRequests.TryRemove(filename, out _);
                throw;
            }
        }
    }
}
