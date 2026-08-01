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
using Fodinae.Networking.Connection.Client;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
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
        private CancellationTokenSource? _loopCts;

        public int PendingAssetCount => _pendingRequests.Count;
        public int QueuedAssetCount => _requestQueue.Count;

        public string[] GetPendingAssetNames()
        {
            return new List<string>(_pendingRequests.Keys).ToArray();
        }

        private Texture2D? _placeholderTexture;
        private Texture2D? _errorTexture;

        [Inject]
        private IConnectionService _connectionService = null!;

        protected void Awake()
        {
            _placeholderTexture = new Texture2D(1, 1);
            _placeholderTexture.SetPixel(0, 0, Color.gray);
            _placeholderTexture.Apply(false, SystemInfo.copyTextureSupport != CopyTextureSupport.None);
            _placeholderTexture.name = "Placeholder_Texture";

            _errorTexture = new Texture2D(1, 1);
            _errorTexture.SetPixel(0, 0, Color.red);
            _errorTexture.Apply(false, SystemInfo.copyTextureSupport != CopyTextureSupport.None);
            _errorTexture.name = "Error_Texture";

            _loopCts = new CancellationTokenSource();
            ProcessBatchLoop(_loopCts.Token).Forget();
        }

        protected void OnDestroy()
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _cache.Clear();

            if (_placeholderTexture != null)
            {
                Destroy(_placeholderTexture);
                _placeholderTexture = null;
            }

            if (_errorTexture != null)
            {
                Destroy(_errorTexture);
                _errorTexture = null;
            }

            if (_connectionService is ConnectionManager cm)
            {
                cm.OnPacketReceived -= OnPacketReceived;
            }
        }

        public UniTask<byte[]?> GetAssetBytesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5)
        {
            return _cache.GetBytesAsync(filename, cancellationToken, timeoutSeconds);
        }

        public async UniTask<string> GetAssetPathAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5)
        {
            var cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            await GetAssetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
            return PersistentAssetCache.GetAssetPath(cleanFilename);
        }

        public async UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            return await _cache.GetTextureAsync(filename, cancellationToken);
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
            if (_placeholderTexture != null)
            {
                applyTextureAction(_placeholderTexture);
            }

            var texture = await GetTextureAsync(filename, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (texture != null)
            {
                applyTextureAction(texture);
            }
            else
            {
                if (!HasAsset(filename))
                {
                    Debug.LogError($"Failed to load texture for '{filename}'. Applying error texture.");
                    if (_errorTexture != null)
                    {
                        applyTextureAction(_errorTexture);
                    }
                }
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        private static async UniTask<byte[]?> LoadBytesFromServerInternal(string filename, CancellationToken ct, int timeoutSeconds)
        {
            var instance = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
            if (instance == null)
            {
                Debug.LogError("[ClientAssetLoader] Cannot load bytes: instance is null");
                return null;
            }

            return await instance.LoadBytesFromServer(filename, ct, timeoutSeconds);
        }

        private async UniTask<byte[]?> LoadBytesFromServer(string filename, CancellationToken ct, int timeoutSeconds)
        {
            filename = filename.TrimStart('/').ToLowerInvariant();

            // 1. Check local RAM/disk cache first when offline
            var cm = ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
            var isConnected = cm != null && cm.Connection != null && cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected;

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
                    Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
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

            return null;
        }

        private static bool IsTextureFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return string.IsNullOrEmpty(ext) || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".tga" || ext == ".bmp";
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
                    var cm = ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
                    if (cm != null && cm.Connection != null &&
                        cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        cm.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
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
            if (obj.Payload is RuntimeAssetPacket assetPacket)
            {
                string filename = assetPacket.Filename.TrimStart('/').ToLowerInvariant();
                if (_pendingRequests.TryRemove(filename, out var tcs))
                {
                    if (assetPacket.Contents.Length == 0 && !string.IsNullOrEmpty(assetPacket.ETag))
                    {
                        var cachedAsset = await GetAssetAsync(assetPacket.Filename).ConfigureAwait(false);
                        tcs.TrySetResult(cachedAsset ?? Array.Empty<byte>());
                    }
                    else
                    {
                        var etag = Calculate(assetPacket.Contents);
                        await SaveAssetAsync(assetPacket.Filename, assetPacket.Contents, etag ?? string.Empty).ConfigureAwait(false);
                        tcs.TrySetResult(assetPacket.Contents);
                    }
                }
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

            var cm = ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
            if (cm == null || cm.Connection == null ||
                cm.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
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
