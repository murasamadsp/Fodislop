using Cysharp.Threading.Tasks;
using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Threading;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using System.Threading.Tasks;
using System.Collections.Generic;
using static PersistentAssetCache;
using static ETagCalculator;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using Fodinae.Assets.Scripts.Networking.Connection;

namespace Fodinae.Assets.Scripts
{
    public class ClientAssetLoader : MonoBehaviour
    {
        public event Action<string, Texture2D> OnTextureLoaded;

        private static ClientAssetLoader _instance;
        public static ClientAssetLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ClientAssetLoader>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[ClientAssetLoader]");
                        _instance = go.AddComponent<ClientAssetLoader>();
                    }
                }
                return _instance;
            }
        }

        private ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();

        private Texture2D _placeholderTexture;
        private Texture2D _errorTexture;

        void Awake()
        {
            // Singleton pattern
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Create a 1x1 gray placeholder texture
            _placeholderTexture = new Texture2D(1, 1);
            _placeholderTexture.SetPixel(0, 0, Color.gray);
            _placeholderTexture.Apply();
            _placeholderTexture.name = "Placeholder_Texture";

            // Create a 1x1 red error texture
            _errorTexture = new Texture2D(1, 1);
            _errorTexture.SetPixel(0, 0, Color.red);
            _errorTexture.Apply();
            _errorTexture.name = "Error_Texture";

            ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
            ConnectionManager.Instance.Connect();
        }

        private void OnPacketReceived(ServerPacket obj)
        {
            if (obj.Payload is RuntimeAssetPacket assetPacket)
            {
                if (_pendingRequests.TryRemove(assetPacket.Filename, out var tcs))
                {
                    if (assetPacket.Contents.Length == 0 && !string.IsNullOrEmpty(assetPacket.ETag))
                    {
                        // Asset is up to date, load from cache
                        var cachedAsset = GetAsset(assetPacket.Filename);
                        tcs.TrySetResult(cachedAsset);
                    }
                    else
                    {
                        var etag = Calculate(assetPacket.Contents);
                        SaveAsset(assetPacket.Filename, assetPacket.Contents, etag);
                        tcs.TrySetResult(assetPacket.Contents);
                    }
                }
            }
        }

        public async UniTaskVoid LoadAndApplyTexture(Action<Texture2D> applyTextureAction, string filename, CancellationToken cancellationToken)
        {
            // 1. Immediately apply the placeholder texture
            applyTextureAction(_placeholderTexture);

            // 2. Check the cache first.
            string etag = null;
            if (HasAsset(filename))
            {
                var cachedAsset = GetAsset(filename);
                if (cachedAsset != null)
                {
                    var texture = await LoadTextureAsync(cachedAsset, cancellationToken);
                    if (texture != null)
                    {
                        applyTextureAction(texture);
                    }
                }
                etag = GetETag(filename);
            }

            // 3. Request the asset from the server
            byte[] imageBytes = await GetAssetBytesFromServer(filename, etag, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            Texture2D loadedTexture = null;
            if (imageBytes != null && imageBytes.Length > 0)
            {
                // 4. Load the texture from the received bytes
                loadedTexture = await LoadTextureAsync(imageBytes, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested) return;

            // 5. Apply the final texture, or an error texture if loading failed.
            if (loadedTexture != null)
            {
                applyTextureAction(loadedTexture);
                OnTextureLoaded?.Invoke(filename, loadedTexture);
            }
            else
            {
                // If the texture failed to load, but we have a cached version, we might want to use it.
                // For now, we just show the error texture.
                if (!HasAsset(filename))
                {
                    Debug.LogError($"Failed to load texture for '{filename}'. Applying error texture.");
                    applyTextureAction(_errorTexture);
                }
            }
        }
        
        private async UniTask<byte[]> GetAssetBytesFromServer(string filename, string etag, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            cancellationToken.Register(() => tcs.TrySetCanceled());

            if (!_pendingRequests.TryAdd(filename, tcs))
            {
                if (_pendingRequests.TryGetValue(filename, out var existingTcs))
                {
                    return await existingTcs.Task;
                }
                return null;
            }
            
            var assetEntry = new RuntimeAssetEntryPacket(filename, etag ?? "");
            var assetRequest = new RuntimeAssetRequestPacket(new List<RuntimeAssetEntryPacket> { assetEntry });
            ConnectionManager.Instance.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));

            return await tcs.Task;
        }

        private async UniTask<Texture2D> LoadTextureAsync(byte[] imageData, CancellationToken cancellationToken)
        {
            await UniTask.SwitchToMainThread(cancellationToken);
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(imageData))
            {
                texture.name = $"Runtime_{DateTime.Now.Ticks}";
                return texture;
            }
            Destroy(texture);
            return null;
        }
    }
}