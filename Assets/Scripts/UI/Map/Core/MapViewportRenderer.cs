#nullable enable

using System;
using Kern.Core;
using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MapViewportRenderer : IDisposable
{
    private readonly Color32[] _cellColorTable = new Color32[256];
    private readonly MapRenderBuffers _buffers = new();

    private ComputeShader? _computeShader;
    private int _kernelHandle = -1;

    private MapChunkGrid _lastGrid;
    private int _lastLoadedChunkCount;
    private bool _hasPackedGrid;

    public Color32[] CellColorTable => _cellColorTable;

    public MapViewportRenderer()
    {
        for (int i = 0; i < 256; i++)
        {
            CellType type = (CellType)i;
            Color32 c = MapBlockColors.GetColor32(type);
            _cellColorTable[i] = c;
            Color cLinear = ((Color)c).linear;
            _buffers.Palette[i] = new Vector4(cLinear.r, cLinear.g, cLinear.b, cLinear.a);
        }
    }

    public void InitColorTable(MapManager manager)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("[MapViewportRenderer] Cannot build color table: map manager is not initialized");
        }

        Color32[] colors = MapProjection.BuildCellColorTable(manager);
        Array.Copy(colors, _cellColorTable, colors.Length);

        for (int i = 0; i < 256; i++)
        {
            Color cLinear = ((Color)_cellColorTable[i]).linear;
            _buffers.Palette[i] = new Vector4(cLinear.r, cLinear.g, cLinear.b, cLinear.a);
        }

        _buffers.UploadPalette();
    }

    // The next render re-packs the visible grid (texture or world replaced).
    public void InvalidateViewState()
    {
        _hasPackedGrid = false;
        _lastLoadedChunkCount = 0;
    }

    public bool Render(
        RenderTexture? mapTexture,
        int worldWidth,
        int worldHeight,
        MapCellSampler cellSampler,
        IMapMipTextureSource? mipSource,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY)
    {
        if (mapTexture == null)
        {
            return false;
        }

        if (texWidth <= 0 || texHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texWidth), "Map texture dimensions must be positive.");
        }

        if (cellsPerPixel <= 0f || float.IsNaN(cellsPerPixel) || float.IsInfinity(cellsPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerPixel), "Map scale must be finite and positive.");
        }

        EnsureComputeResources();

        MapChunkGrid grid = MapChunkGrid.Compute(
            worldWidth,
            worldHeight,
            texWidth,
            texHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);
        bool useMip = mipSource is { IsReady: true } &&
                      grid.Area > MapViewportChunkBudget.MaxPackedChunkSlots;
        if (grid.Area > MapViewportChunkBudget.MaxPackedChunkSlots && !useMip)
        {
            // The overview is being prepared (or failed). Packing a partial
            // world-sized grid here walks every loaded storage chunk and can
            // repack up to the full GPU slot budget on the main thread. Keep
            // the current texture until the mip source requests a render.
            return false;
        }

        int chunkSlot;
        if (useMip)
        {
            _buffers.EnsureChunkBuffers(1, 1);
            chunkSlot = 0;
        }
        else
        {
            chunkSlot = PackVisibleChunks(cellSampler, grid);
        }

        Texture mipTextureToBind = useMip ? mipSource!.Texture : _buffers.GetDummyMipTexture();

        _computeShader!.SetVector("_StartWorld", new Vector4(grid.StartWorldX, grid.StartWorldY, 0f, 0f));
        _computeShader.SetVector("_TexSize", new Vector4(texWidth, texHeight, 0f, 0f));
        _computeShader.SetFloat("_CellsPerPixel", cellsPerPixel);
        _computeShader.SetInt("_WorldWidth", worldWidth);
        _computeShader.SetInt("_WorldHeight", worldHeight);
        _computeShader.SetInt("_UseMip", useMip ? 1 : 0);
        _computeShader.SetInt("_MipLevelCount", useMip ? mipSource!.MipLevelCount : 1);
        _computeShader.SetInt("_MipBlockSize", useMip ? mipSource!.MipBlockSize : 1);
        _computeShader.SetInt("_GridMinChunkX", grid.MinChunkX);
        _computeShader.SetInt("_GridMinChunkY", grid.MinChunkY);
        _computeShader.SetInt("_GridWidth", grid.Width);
        _computeShader.SetInt("_GridHeight", grid.Height);

        _computeShader.SetTexture(_kernelHandle, "_Result", mapTexture);
        _computeShader.SetTexture(_kernelHandle, "_MipTexture", mipTextureToBind);
        _computeShader.SetBuffer(_kernelHandle, "_Palette", _buffers.PaletteBuffer);
        _computeShader.SetBuffer(_kernelHandle, "_ChunkData", _buffers.ChunkDataBuffer);
        _computeShader.SetBuffer(_kernelHandle, "_ChunkLookup", _buffers.ChunkLookupBuffer);

        int groupsX = Mathf.CeilToInt(texWidth / 8f);
        int groupsY = Mathf.CeilToInt(texHeight / 8f);
        _computeShader.Dispatch(_kernelHandle, groupsX, groupsY, 1);
        return chunkSlot > 0 || useMip;
    }

    public void Dispose() => _buffers.Dispose();

    private int PackVisibleChunks(MapCellSampler cellSampler, MapChunkGrid grid)
    {
        int gridArea = grid.Area;
        _buffers.EnsureChunkBuffers(gridArea, gridArea);

        bool fullRefresh = !_hasPackedGrid || !grid.Matches(_lastGrid) ||
                           cellSampler.RequiresFullRefresh ||
                           _buffers.ChunkLookupBuffer.count < gridArea;
        if (fullRefresh)
        {
            _lastGrid = grid;
            _lastLoadedChunkCount = PackDenseGrid(cellSampler, grid);
            _buffers.UploadLookup(gridArea);
            UploadLoadedChunkRuns(gridArea);
            cellSampler.CompleteRefresh();
            _hasPackedGrid = true;
            return _lastLoadedChunkCount;
        }

        if (cellSampler.DirtyChunks.Count == 0)
        {
            return _lastLoadedChunkCount;
        }

        PatchDirtyChunks(cellSampler, grid);
        cellSampler.CompleteRefresh();
        return _lastLoadedChunkCount;
    }

    private int PackDenseGrid(MapCellSampler cellSampler, MapChunkGrid grid)
    {
        int storageChunkSize = cellSampler.ChunkSize;
        int subChunksPerStorageChunk = MapGpuChunkPacker.GetSubChunksPerStorageChunk(storageChunkSize);
        int[] lookup = _buffers.ChunkLookup;
        uint[] data = _buffers.ChunkData;
        int loadedCount = 0;
        for (int gy = 0; gy < grid.Height; gy++)
        {
            int chunkY = grid.MinChunkY + gy;
            int rowOffset = gy * grid.Width;
            for (int gx = 0; gx < grid.Width; gx++)
            {
                int gpuChunkX = grid.MinChunkX + gx;
                MapGpuChunkPacker.GetStorageChunkAddress(
                    gpuChunkX,
                    chunkY,
                    subChunksPerStorageChunk,
                    out int storageChunkX,
                    out int storageChunkY,
                    out int subChunkX,
                    out int subChunkY);
                int slot = rowOffset + gx;
                if (cellSampler.TryGetChunk(storageChunkX, storageChunkY, out CellType[]? chunk) && chunk != null)
                {
                    lookup[slot] = slot;
                    MapGpuChunkPacker.PackSubChunk(
                        chunk,
                        storageChunkSize,
                        subChunkX,
                        subChunkY,
                        data,
                        slot);
                    loadedCount++;
                }
                else
                {
                    lookup[rowOffset + gx] = -1;
                }
            }
        }

        return loadedCount;
    }

    private void UploadLoadedChunkRuns(int gridArea)
    {
        int[] lookup = _buffers.ChunkLookup;
        int runStart = -1;
        for (int slot = 0; slot <= gridArea; slot++)
        {
            bool loaded = slot < gridArea && lookup[slot] >= 0;
            if (loaded && runStart < 0)
            {
                runStart = slot;
            }
            else if (!loaded && runStart >= 0)
            {
                _buffers.UploadChunkDataRange(
                    runStart * MapGpuChunkPacker.PackedUIntCount,
                    (slot - runStart) * MapGpuChunkPacker.PackedUIntCount);
                runStart = -1;
            }
        }
    }

    private void PatchDirtyChunks(
        MapCellSampler cellSampler,
        MapChunkGrid grid)
    {
        if (cellSampler.Layer == null || cellSampler.HeightChunks <= 0)
        {
            return;
        }

        int[] lookup = _buffers.ChunkLookup;
        uint[] data = _buffers.ChunkData;
        int subChunksPerStorageChunk = MapGpuChunkPacker.GetSubChunksPerStorageChunk(cellSampler.ChunkSize);
        int firstStorageChunkX = Math.Max(0, grid.MinChunkX / subChunksPerStorageChunk);
        int lastStorageChunkX = (grid.MinChunkX + grid.Width - 1) / subChunksPerStorageChunk;
        int firstStorageChunkY = Math.Max(0, grid.MinChunkY / subChunksPerStorageChunk);
        int lastStorageChunkY = (grid.MinChunkY + grid.Height - 1) / subChunksPerStorageChunk;
        int heightChunks = cellSampler.HeightChunks;
        foreach (int chunkIndex in cellSampler.DirtyChunks)
        {
            int storageChunkX = chunkIndex / heightChunks;
            int storageChunkY = chunkIndex % heightChunks;
            if (storageChunkX < firstStorageChunkX || storageChunkX > lastStorageChunkX ||
                storageChunkY < firstStorageChunkY || storageChunkY > lastStorageChunkY)
            {
                continue;
            }

            bool available = cellSampler.TryGetChunk(storageChunkX, storageChunkY, out CellType[]? chunk) && chunk != null;
            int gpuChunkXStart = storageChunkX * subChunksPerStorageChunk;
            int gpuChunkYStart = storageChunkY * subChunksPerStorageChunk;

            for (int subChunkY = 0; subChunkY < subChunksPerStorageChunk; subChunkY++)
            {
                int gpuChunkY = gpuChunkYStart + subChunkY;
                int gy = gpuChunkY - grid.MinChunkY;
                if (gy < 0 || gy >= grid.Height)
                {
                    continue;
                }

                int firstSubChunkX = Math.Max(0, grid.MinChunkX - gpuChunkXStart);
                int lastSubChunkX = Math.Min(subChunksPerStorageChunk - 1,
                    grid.MinChunkX + grid.Width - 1 - gpuChunkXStart);
                int firstSlot = -1;
                int slotCount = 0;
                for (int subChunkX = firstSubChunkX; subChunkX <= lastSubChunkX; subChunkX++)
                {
                    int gpuChunkX = gpuChunkXStart + subChunkX;
                    int gx = gpuChunkX - grid.MinChunkX;
                    int slot = gy * grid.Width + gx;
                    bool wasAvailable = lookup[slot] >= 0;
                    if (firstSlot < 0)
                    {
                        firstSlot = slot;
                    }

                    if (available)
                    {
                        MapGpuChunkPacker.PackSubChunk(chunk!, cellSampler.ChunkSize, subChunkX, subChunkY, data, slot);
                        lookup[slot] = slot;
                        if (!wasAvailable)
                        {
                            _lastLoadedChunkCount++;
                        }
                    }
                    else
                    {
                        lookup[slot] = -1;
                        if (wasAvailable)
                        {
                            _lastLoadedChunkCount--;
                        }
                    }
                    slotCount++;
                }

                if (slotCount > 0)
                {
                    _buffers.UploadLookupRange(firstSlot, slotCount);
                    if (available)
                    {
                        _buffers.UploadChunkDataRange(
                            firstSlot * MapGpuChunkPacker.PackedUIntCount,
                            slotCount * MapGpuChunkPacker.PackedUIntCount);
                    }
                }
            }
        }
    }

    private void EnsureComputeResources()
    {
        if (_computeShader != null)
        {
            return;
        }

        _computeShader = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldMapCompute) ??
            throw new InvalidOperationException(
                $"[MapViewportRenderer] Resources/{ProjectRuntimeContracts.ResourcePaths.WorldMapCompute}.compute is missing.");
        _kernelHandle = _computeShader.FindKernel("CSWorldMapRender");
    }
}
