#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core;
using Fodinae.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using Fodinae.World.Streaming;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldSimulationState(
    IAsyncOperationSupervisor operations,
    IDummyWorldMapSource worldMaps) : IDisposable
{
    private readonly IDummyWorldMapSource _worldMaps = worldMaps ??
        throw new ArgumentNullException(nameof(worldMaps));
    private readonly IAsyncOperationSupervisor _operations = operations ??
        throw new ArgumentNullException(nameof(operations));
    private readonly HashSet<int> _sentMapChunks = new();
    private readonly SemaphoreSlim _streamingGate = new(1, 1);
    private StreamingWindow _residentWindow = new(Vector2Int.one * int.MinValue, Vector2Int.zero);
    private CancellationTokenSource? _activeStreamingRequest;
    private CancellationTokenSource? _terrainRequestCancellation;
    private CancellationTokenSource? _activeOpenCancellation;
    private LayerLease? _layerLease;
    private RectInt? _requestedTerrainRegion;
    private string? _worldCodeName;
    private UniTaskCompletionSource? _initializationInFlight;
    private bool _initialized;
    private bool _disposed;
    private int _inFlightOperations;
    private int _resourcesDisposed;

    public WorldLayer<CellType>? Layer => _layerLease?.Layer;

    public CellConfigurationPacket[]? CellConfigurations { get; private set; }

    public bool HasLayer => _layerLease != null;

    public async UniTask EnsureInitializedAsync(Func<UniTask> initialize)
    {
        if (initialize == null)
        {
            throw new ArgumentNullException(nameof(initialize));
        }

        UniTaskCompletionSource? inFlight = _initializationInFlight;
        if (inFlight != null)
        {
            await inFlight.Task;
            return;
        }

        if (_initialized)
        {
            return;
        }

        var gate = new UniTaskCompletionSource();
        _initializationInFlight = gate;
        try
        {
            await initialize();
            _initialized = true;
            gate.TrySetResult();
        }
        catch (Exception exception)
        {
            _initialized = false;
            gate.TrySetException(exception);
            throw;
        }
        finally
        {
            _initializationInFlight = null;
        }
    }

    public async UniTask<DummyWorldDescriptor> OpenAsync(
        string worldCodeName,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        CancelTerrainRequest();
        CancelRequest(ref _activeStreamingRequest);
        _worldCodeName = null;

        using CancellationTokenSource openCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        ReplaceRequest(ref _activeOpenCancellation, openCts);
        Interlocked.Increment(ref _inFlightOperations);

        try
        {
            using (await AcquireStreamingGateAsync(openCts.Token))
            {
                openCts.Token.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                DisposeLayer();

                string mapPath = await _worldMaps.GetMapFileAsync(worldCodeName, openCts.Token);

                openCts.Token.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                (int worldWidth, int worldHeight) =
                    await DummyWorldMapArchive.ReadDimensionsWithRetryAsync(mapPath);

                openCts.Token.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                if (worldWidth <= 0 || worldHeight <= 0)
                {
                    throw new InvalidDataException(
                        $"Prebaked map file '{mapPath}' has invalid dimensions ({worldWidth}x{worldHeight}).");
                }

                int widthChunks = (worldWidth + ProjectRuntimeContracts.World.ChunkSize - 1) /
                    ProjectRuntimeContracts.World.ChunkSize;
                int heightChunks = (worldHeight + ProjectRuntimeContracts.World.ChunkSize - 1) /
                    ProjectRuntimeContracts.World.ChunkSize;

                WorldLayer<CellType>? newLayer = null;
                try
                {
                    newLayer = new WorldLayer<CellType>(
                        mapPath,
                        widthChunks,
                        heightChunks,
                        _operations,
                        ProjectRuntimeContracts.World.ChunkSize,
                        maxRamChunks: ProjectRuntimeContracts.World.ResidentChunkCacheCapacity);

                    openCts.Token.ThrowIfCancellationRequested();
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                    }

                    CellConfigurationPacket[] cellConfigs =
                        DummyCellConfigurationUtilities.CreateCellConfigurations();
                    CellConfigurations = cellConfigs;
                    _layerLease = new LayerLease(newLayer);
                    newLayer = null;
                    _sentMapChunks.Clear();
                    _residentWindow = new StreamingWindow(Vector2Int.one * int.MinValue, Vector2Int.zero);
                    _worldCodeName = worldCodeName;
                    return new DummyWorldDescriptor(worldWidth, worldHeight, cellConfigs);
                }
                finally
                {
                    newLayer?.Dispose();
                }
            }
        }
        finally
        {
            TryCompleteRequest(ref _activeOpenCancellation, openCts);
            OnOperationCompleted();
        }
    }

    public void QueueTerrainRegion(string worldCodeName, RectInt region, Action<ServerPacket> sendPacket)
    {
        if (_disposed || _worldCodeName != worldCodeName ||
            region.width <= 0 || region.height <= 0 || _requestedTerrainRegion == region)
        {
            return;
        }

        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return;
        }

        var request = new CancellationTokenSource();
        ReplaceRequest(ref _terrainRequestCancellation, request);
        _requestedTerrainRegion = region;
        Interlocked.Increment(ref _inFlightOperations);
        try
        {
            _operations.Run(
                "dummy_stream_terrain",
                token => SendTerrainRegionAsync(lease, worldCodeName, region, sendPacket, request, token));
        }
        catch
        {
            lease.Release();
            if (TryCompleteRequest(ref _terrainRequestCancellation, request))
            {
                _requestedTerrainRegion = null;
            }

            request.Dispose();
            OnOperationCompleted();
            throw;
        }
    }

    private async UniTask SendTerrainRegionAsync(
        LayerLease lease,
        string worldCodeName,
        RectInt region,
        Action<ServerPacket> sendPacket,
        CancellationTokenSource request,
        CancellationToken supervisorToken)
    {
        try
        {
            using (request)
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, supervisorToken))
            {
                bool completed = false;
                try
                {
                    using (await AcquireStreamingGateAsync(linked.Token))
                    {
                        if (_disposed || _worldCodeName != worldCodeName)
                        {
                            return;
                        }

                        linked.Token.ThrowIfCancellationRequested();
                        await DummyMapStreamer.SendMapWindowAsync(
                            lease.Layer,
                            _sentMapChunks,
                            new StreamingWindow(region.position, region.size),
                            sendPacket,
                            linked.Token);
                        completed = true;
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    // Replaced viewport or world shutdown; no partial packet was published.
                }
                catch (ObjectDisposedException) when (_disposed || linked.IsCancellationRequested)
                {
                    // Gate was disposed during shutdown or cancellation.
                }
                finally
                {
                    if (TryCompleteRequest(ref _terrainRequestCancellation, request) && !completed)
                    {
                        _requestedTerrainRegion = null;
                    }
                }
            }
        }
        finally
        {
            lease.Release();
            OnOperationCompleted();
        }
    }

    private void CancelTerrainRequest()
    {
        CancelRequest(ref _terrainRequestCancellation);
        _requestedTerrainRegion = null;
    }

    public CellType GetCell(ushort serverX, ushort serverY)
    {
        // Simulation queries must never turn a movement/action packet into a
        // synchronous disk read. The caller can retry after the chunk has
        // arrived through ReadChunk().
        return TryGetCell(serverX, serverY, out CellType cellType)
            ? cellType
            : CellType.Unloaded;
    }

    public bool TryGetCell(ushort serverX, ushort serverY, out CellType cellType)
    {
        WorldLayer<CellType>? layer = Layer;
        if (layer != null && layer.TryGetCell(serverX, serverY, out cellType))
        {
            return true;
        }

        cellType = CellType.Unloaded;
        return false;
    }

    public async UniTask<bool> EnsureCellAvailableAsync(
        ushort serverX,
        ushort serverY,
        CancellationToken cancellationToken = default)
    {
        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return false;
        }

        try
        {
            IWorldLayer<CellType> layer = lease.Layer;
            if (!layer.GetChunkIndexAndLocal(serverX, serverY, out int chunkIndex, out _))
            {
                return false;
            }

            ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: true);
            while (result.Status == ChunkReadStatus.Loading)
            {
                if (_disposed)
                {
                    return false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                result = layer.ReadChunk(chunkIndex, touchLru: true);
            }

            return result.Status == ChunkReadStatus.Available;
        }
        finally
        {
            lease.Release();
        }
    }

    public CellConfigurationPacket? GetCellConfig(CellType type)
    {
        int index = (int)type;
        CellConfigurationPacket[]? configs = CellConfigurations;
        if (configs == null || index < 0 || index >= configs.Length)
        {
            return null;
        }

        return configs[index];
    }

    public void SetCell(ushort serverX, ushort serverY, CellType type)
    {
        WorldLayer<CellType>? layer = Layer;
        if (layer != null)
        {
            layer[serverX, serverY] = type;
        }
    }

    public async UniTask SendChunksAroundAsync(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? worldCodeName = _worldCodeName;
        if (worldCodeName == null)
        {
            return;
        }

        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return;
        }

        try
        {
            if (_disposed || _worldCodeName != worldCodeName)
            {
                return;
            }

            if (!DummyMapStreamer.NeedsStreaming(
                    lease.Layer,
                    _residentWindow,
                    playerX,
                    playerY))
            {
                return;
            }

            using CancellationTokenSource requestCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ReplaceRequest(ref _activeStreamingRequest, requestCancellation);
            Interlocked.Increment(ref _inFlightOperations);

            try
            {
                using (await AcquireStreamingGateAsync(requestCancellation.Token))
                {
                    if (_disposed || _worldCodeName != worldCodeName)
                    {
                        return;
                    }

                    requestCancellation.Token.ThrowIfCancellationRequested();

                    _residentWindow = await DummyMapStreamer.SendMapChunksAroundAsync(
                        lease.Layer,
                        _sentMapChunks,
                        _residentWindow,
                        playerX,
                        playerY,
                        sendPacket,
                        requestCancellation.Token);
                }
            }
            catch (ObjectDisposedException) when (_disposed || requestCancellation.IsCancellationRequested)
            {
                // Gate was disposed during shutdown or cancellation.
            }
            finally
            {
                TryCompleteRequest(ref _activeStreamingRequest, requestCancellation);
                OnOperationCompleted();
            }
        }
        finally
        {
            lease.Release();
        }
    }

    public void QueueChunksAround(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket)
    {
        _operations.Run(
            "dummy_stream_chunks",
            cancellationToken => SendChunksAroundAsync(
                playerX,
                playerY,
                sendPacket,
                cancellationToken));
    }

    public void Reset()
    {
        CancelTerrainRequest();
        _worldCodeName = null;
        CancelRequest(ref _activeStreamingRequest);
        CancelRequest(ref _activeOpenCancellation);
        _initialized = false;
        DisposeLayer();
        CellConfigurations = null;
        _sentMapChunks.Clear();
        _residentWindow = new StreamingWindow(Vector2Int.one * int.MinValue, Vector2Int.zero);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Reset();
        if (Volatile.Read(ref _inFlightOperations) == 0)
        {
            DisposeResources();
        }
    }

    private void OnOperationCompleted()
    {
        if (Interlocked.Decrement(ref _inFlightOperations) == 0 && _disposed)
        {
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _streamingGate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        DisposeLayer();
    }

    private LayerLease? AcquireLayerLease()
    {
        LayerLease? lease = Volatile.Read(ref _layerLease);
        if (lease != null && lease.TryAddRef())
        {
            return lease;
        }

        return null;
    }

    private void DisposeLayer()
    {
        LayerLease? lease = Interlocked.Exchange(ref _layerLease, null);
        lease?.Release();
    }

    private async UniTask<StreamingGateLock> AcquireStreamingGateAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _streamingGate.WaitAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            try
            {
                _streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }

            throw new OperationCanceledException(cancellationToken);
        }

        if (_disposed)
        {
            try
            {
                _streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }

            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        return new StreamingGateLock(this);
    }

    private static void ReplaceRequest(
        ref CancellationTokenSource? activeRequest,
        CancellationTokenSource newRequest)
    {
        CancellationTokenSource? previous = Interlocked.Exchange(ref activeRequest, newRequest);
        if (previous != null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static void CancelRequest(ref CancellationTokenSource? activeRequest)
    {
        CancellationTokenSource? current = Interlocked.Exchange(ref activeRequest, null);
        if (current != null)
        {
            try
            {
                current.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static bool TryCompleteRequest(
        ref CancellationTokenSource? activeRequest,
        CancellationTokenSource expectedRequest)
    {
        return Interlocked.CompareExchange(ref activeRequest, null, expectedRequest) == expectedRequest;
    }

    private sealed class LayerLease(WorldLayer<CellType> layer)
    {
        private int _refCount = 1;

        public WorldLayer<CellType> Layer => layer;

        public bool TryAddRef()
        {
            while (true)
            {
                int current = Volatile.Read(ref _refCount);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                layer.Dispose();
            }
        }
    }

    private readonly struct StreamingGateLock(DummyWorldSimulationState owner) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                owner._streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Gate was disposed concurrently during release.
            }
        }
    }
}

internal readonly record struct DummyWorldDescriptor(
    int Width,
    int Height,
    CellConfigurationPacket[] CellConfigurations);
