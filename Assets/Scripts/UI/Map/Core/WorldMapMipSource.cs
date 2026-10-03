#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

/// <summary>
/// Owns the map overview mip pyramid and its texture: asynchronous initial
/// population from the stored layer file, plus bounded live updates from chunk
/// notifications. Implements <see cref="IMapMipTextureSource"/> so the viewport
/// renderer never depends on this concrete type.
/// </summary>
internal sealed class WorldMapMipSource : IMapMipTextureSource, IDisposable
{
    private const int PendingBatchSize = 8;

    private readonly ConcurrentQueue<int> _pendingChunks = new();
    private Action? _requestRender;
    private WorldMapMipPyramid? _pyramid;
    private Texture2D? _texture;
    private IWorldLayer<CellType>? _cellLayer;
    private CancellationTokenSource? _scanCancellation;
    private int _mipBlockSize;
    private bool _textureDirty = true;
    private bool _failed;
    private int _progress;
    private int _total;

    public bool IsReady { get; private set; }

    public bool Failed => _failed;

    public int Progress => Volatile.Read(ref _progress);

    public int Total => Volatile.Read(ref _total);

    public int MipLevelCount => _pyramid?.MipLevelCount ?? 0;

    public int MipBlockSize => _mipBlockSize;

    public Texture Texture
    {
        get
        {
            EnsureTexture();
            return _texture!;
        }
    }

    public void SetRequestRenderCallback(Action requestRender) =>
        _requestRender = requestRender ?? throw new ArgumentNullException(nameof(requestRender));

    public void Bind(
        IWorldLayer<CellType>? cellLayer,
        int chunkSize,
        int worldWidth,
        int worldHeight,
        Color32[] cellColorTable)
    {
        CancelScan();
        DisposeResources();

        _cellLayer = cellLayer;
        IsReady = false;
        _failed = false;
        _progress = 0;
        _total = 0;
        while (_pendingChunks.TryDequeue(out _))
        {
        }

        int widthChunks = cellLayer?.WidthChunks ?? 0;
        int heightChunks = cellLayer?.HeightChunks ?? 0;
        if (widthChunks <= 0 || heightChunks <= 0 || chunkSize <= 0 || worldWidth <= 0 || worldHeight <= 0)
        {
            return;
        }

        try
        {
            _mipBlockSize = WorldMapMipPyramid.ChooseBlockSize(worldWidth, worldHeight, chunkSize);
            _pyramid = new WorldMapMipPyramid(
                widthChunks,
                heightChunks,
                chunkSize,
                worldWidth,
                worldHeight,
                cellColorTable,
                MapProjection.UnknownCellSolidColor,
                _mipBlockSize);
        }
        catch (InvalidOperationException exception)
        {
            _failed = true;
            Debug.LogException(exception);
        }
    }

    public void Begin()
    {
        if (IsReady || _failed || _scanCancellation != null || _pyramid == null || _cellLayer == null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        _ = PrepareAsync(_scanCancellation);
    }

    public void QueueChunk(int chunkX, int chunkY)
    {
        WorldMapMipPyramid? pyramid = _pyramid;
        if (pyramid == null || chunkX < 0 || chunkY < 0 ||
            chunkX >= pyramid.WidthChunks || chunkY >= pyramid.HeightChunks)
        {
            return;
        }

        _pendingChunks.Enqueue(chunkY + (chunkX * pyramid.HeightChunks));
    }

    public void ApplyPending()
    {
        if (!IsReady || _pyramid == null || _cellLayer == null)
        {
            return;
        }

        bool changed = false;
        int processed = 0;
        while (processed < PendingBatchSize && _pendingChunks.TryDequeue(out int chunkIndex))
        {
            processed++;
            ChunkReadResult<CellType> result = _cellLayer.ReadChunk(chunkIndex, touchLRU: false);
            if (result.Status == ChunkReadStatus.Available && result.Data != null)
            {
                _pyramid.SetChunkCells(chunkIndex, result.Data);
                changed = true;
            }
        }

        if (changed)
        {
            _textureDirty = true;
            _requestRender?.Invoke();
        }
    }

    public void Dispose()
    {
        CancelScan();
        DisposeResources();
    }

    private async UniTask PrepareAsync(CancellationTokenSource scanCancellation)
    {
        CancellationToken cancellationToken = scanCancellation.Token;
        WorldMapMipPyramid? pyramid = _pyramid;
        IWorldLayer<CellType>? layer = _cellLayer;
        if (pyramid == null || layer == null)
        {
            return;
        }

        try
        {
            if (layer is IStoredChunkSource<CellType> source)
            {
                await source.VisitStoredChunkRunsAsync(
                    (chunkIndex, cellType, runLength) => pyramid.AddStoredRun(chunkIndex, cellType, runLength),
                    (progress, total) =>
                    {
                        Volatile.Write(ref _progress, progress);
                        Volatile.Write(ref _total, total);
                    },
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, pyramid, layer))
            {
                return;
            }

            pyramid.CompleteStoredScan();
            foreach (int chunkIndex in layer.GetLoadedChunkIndices())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLRU: false);
                if (result.Status == ChunkReadStatus.Available && result.Data != null)
                {
                    pyramid.SetChunkCells(chunkIndex, result.Data);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, pyramid, layer))
            {
                return;
            }

            IsReady = true;
            _requestRender?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentScan(scanCancellation, pyramid, layer))
            {
                Debug.LogException(exception);
                _failed = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation))
            {
                _scanCancellation = null;
            }

            scanCancellation.Dispose();
        }
    }

    private void EnsureTexture()
    {
        WorldMapMipPyramid? pyramid = _pyramid;
        if (pyramid == null)
        {
            throw new InvalidOperationException(
                "[WorldMapMipSource] The mip texture was requested before the pyramid was bound.");
        }

        if (_texture == null)
        {
            _texture = RuntimeTextureFactory.CreateRGBA32MipChain(
                pyramid.GetLevelWidth(0),
                pyramid.GetLevelHeight(0),
                "WorldMapMipTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _textureDirty = true;
        }

        if (_textureDirty)
        {
            for (int level = 0; level < pyramid.MipLevelCount; level++)
            {
                _texture.SetPixelData(pyramid.GetLevel(level), level);
            }

            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _textureDirty = false;
        }
    }

    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }

    private void DisposeResources()
    {
        _pyramid = null;
        _cellLayer = null;
        _textureDirty = true;
        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }
    }

    private bool IsCurrentScan(
        CancellationTokenSource scanCancellation,
        WorldMapMipPyramid pyramid,
        IWorldLayer<CellType> layer) =>
        ReferenceEquals(_scanCancellation, scanCancellation) &&
        ReferenceEquals(_pyramid, pyramid) &&
        ReferenceEquals(_cellLayer, layer);
}
