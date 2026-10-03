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
    private int _lastSamplerRevision = -1;
    private int _lastLoadedChunkCount;

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

    // The next render re-uploads the visible chunk grid even when its bounds
    // and the sampler revision are unchanged (texture or world replaced).
    public void InvalidateViewState()
    {
        _lastGrid = default;
        _lastSamplerRevision = -1;
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
        _buffers.EnsureChunkBuffers(gridArea, MapViewportChunkBudget.MaxPackedChunkSlots);

        bool gridUnchanged = grid.Matches(_lastGrid) &&
                             cellSampler.Revision == _lastSamplerRevision &&
                             _buffers.ChunkLookupBuffer.count >= gridArea;
        if (gridUnchanged)
        {
            return _lastLoadedChunkCount;
        }

        _lastGrid = grid;
        _lastSamplerRevision = cellSampler.Revision;

        int chunkSlot = PackGrid(cellSampler, grid);
        _lastLoadedChunkCount = chunkSlot;
        _buffers.UploadLookup(gridArea);
        if (chunkSlot > 0)
        {
            _buffers.UploadChunkData(chunkSlot * MapGpuChunkPacker.PackedUIntCount);
        }

        return chunkSlot;
    }

    private int PackGrid(MapCellSampler cellSampler, MapChunkGrid grid)
    {
        int storageChunkSize = cellSampler.ChunkSize;
        int subChunksPerStorageChunk = MapGpuChunkPacker.GetSubChunksPerStorageChunk(storageChunkSize);
        int[] lookup = _buffers.ChunkLookup;
        uint[] data = _buffers.ChunkData;

        if (grid.Area <= MapViewportChunkBudget.MaxPackedChunkSlots)
        {
            return PackDenseGrid(cellSampler, grid, subChunksPerStorageChunk, storageChunkSize, lookup, data);
        }

        return PackLoadedChunks(cellSampler, grid, subChunksPerStorageChunk, storageChunkSize, lookup, data);
    }

    private static int PackDenseGrid(
        MapCellSampler cellSampler,
        MapChunkGrid grid,
        int subChunksPerStorageChunk,
        int storageChunkSize,
        int[] lookup,
        uint[] data)
    {
        int chunkSlot = 0;
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
                if (cellSampler.TryGetChunk(storageChunkX, storageChunkY, out CellType[]? chunk) &&
                    chunk != null && chunkSlot < MapViewportChunkBudget.MaxPackedChunkSlots)
                {
                    lookup[rowOffset + gx] = chunkSlot;
                    MapGpuChunkPacker.PackSubChunk(
                        chunk,
                        storageChunkSize,
                        subChunkX,
                        subChunkY,
                        data,
                        chunkSlot);
                    chunkSlot++;
                }
                else
                {
                    lookup[rowOffset + gx] = -1;
                }
            }
        }

        return chunkSlot;
    }

    private static int PackLoadedChunks(
        MapCellSampler cellSampler,
        MapChunkGrid grid,
        int subChunksPerStorageChunk,
        int storageChunkSize,
        int[] lookup,
        uint[] data)
    {
        int gridArea = grid.Area;
        Array.Fill(lookup, -1, 0, gridArea);
        if (cellSampler.Layer == null || cellSampler.HeightChunks <= 0)
        {
            return 0;
        }

        int gridMaxChunkX = grid.MinChunkX + grid.Width;
        int gridMaxChunkY = grid.MinChunkY + grid.Height;
        int heightChunks = cellSampler.HeightChunks;
        int chunkSlot = 0;
        foreach (int chunkIndex in cellSampler.Layer.GetLoadedChunkIndices())
        {
            int storageChunkX = chunkIndex / heightChunks;
            int storageChunkY = chunkIndex % heightChunks;
            int minGpuChunkX = storageChunkX * subChunksPerStorageChunk;
            int maxGpuChunkX = minGpuChunkX + subChunksPerStorageChunk - 1;
            int minGpuChunkY = storageChunkY * subChunksPerStorageChunk;
            int maxGpuChunkY = minGpuChunkY + subChunksPerStorageChunk - 1;

            if (maxGpuChunkX < grid.MinChunkX || minGpuChunkX >= gridMaxChunkX ||
                maxGpuChunkY < grid.MinChunkY || minGpuChunkY >= gridMaxChunkY)
            {
                continue;
            }

            if (!cellSampler.TryGetChunk(storageChunkX, storageChunkY, out CellType[]? chunk) || chunk == null)
            {
                continue;
            }

            for (int subChunkY = 0; subChunkY < subChunksPerStorageChunk; subChunkY++)
            {
                int gpuChunkY = storageChunkY * subChunksPerStorageChunk + subChunkY;
                int gy = gpuChunkY - grid.MinChunkY;
                if (gy < 0 || gy >= grid.Height)
                {
                    continue;
                }

                for (int subChunkX = 0; subChunkX < subChunksPerStorageChunk; subChunkX++)
                {
                    int gpuChunkX = storageChunkX * subChunksPerStorageChunk + subChunkX;
                    int gx = gpuChunkX - grid.MinChunkX;
                    if (gx < 0 || gx >= grid.Width || chunkSlot >= MapViewportChunkBudget.MaxPackedChunkSlots)
                    {
                        continue;
                    }

                    lookup[gy * grid.Width + gx] = chunkSlot;
                    MapGpuChunkPacker.PackSubChunk(
                        chunk,
                        storageChunkSize,
                        subChunkX,
                        subChunkY,
                        data,
                        chunkSlot);
                    chunkSlot++;
                }
            }
        }

        return chunkSlot;
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
