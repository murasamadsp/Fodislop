#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.World.Textures;

internal sealed class TerrainDecalAtlasLoader
{
    private readonly string _filename;
    private readonly string _operationKey;
    private bool _loadStarted;

    public Texture2D? AtlasTexture { get; private set; }

    public TerrainDecalAtlasLoader(string filename, string operationKey)
    {
        _filename = filename;
        _operationKey = operationKey;
    }

    public void StartLoad(
        ITextureStorageService textureStorage,
        IAsyncOperationSupervisor operations,
        Action<string, Texture2D>? onTextureLoaded)
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        operations.Run(
            _operationKey,
            cancellationToken => LoadAsync(textureStorage, onTextureLoaded, cancellationToken));
    }

    private async UniTask LoadAsync(
        ITextureStorageService textureStorage,
        Action<string, Texture2D>? onTextureLoaded,
        CancellationToken cancellationToken)
    {
        Texture2D texture = await textureStorage.GetTextureAsync(
            _filename,
            cancellationToken) ??
            throw new InvalidOperationException(
                $"Required terrain decal atlas '{_filename}' could not be decoded.");

        if (texture.width != 512 || texture.height != 32)
        {
            throw new InvalidOperationException(
                $"Terrain decal atlas must be 512x32, got {texture.width}x{texture.height}.");
        }

        RuntimeTextureFactory.ApplySampling(
            texture,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        AtlasTexture = texture;
        onTextureLoaded?.Invoke(_filename, texture);
    }

    public void Dispose()
    {
        AtlasTexture = null;
    }
}
