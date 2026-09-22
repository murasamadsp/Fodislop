#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;
// Реализует ICachedCellDataProvider сам: заливке фона нужен тип и свойства
// клетки, и брать их больше неоткуда. Раньше переходником служил
// TerrainRenderer — MonoBehaviour в роли адаптера над собственным полем.
public class TerrainCellCache : ICachedCellDataProvider
{
    private readonly TerrainRingGrid<CachedCellData> _cellCache = new();
    private int _cacheMinX = int.MinValue;
    private int _cacheMinY = int.MinValue;
    private int _cacheWidth;
    private int _cacheHeight;
    private readonly CellTypeSpatialIndex _cellsByType = new();
    private readonly List<(long Key, CellType Type)> _refreshEntries = [];
    private readonly TerrainCellMetadataCache _metadataCache = new();

    private static CachedCellData _UnloadedCellData => new()
    {
        State = TerrainCellState.Unloaded,
        Type = CellType.Unloaded,
        AtlasIndex = -1,
    };

    public int CacheMinX => _cacheMinX;
    public int CacheMinY => _cacheMinY;
    public int CacheWidth => _cacheWidth;
    public int CacheHeight => _cacheHeight;

    public void EnsureCapacity(int width, int height)
    {
        _cacheWidth = width + 2;
        _cacheHeight = height + 2;
        if (_cellCache.Width != _cacheWidth || _cellCache.Height != _cacheHeight)
        {
            _cellCache.EnsureSize(_cacheWidth, _cacheHeight);
            _cellsByType.EnsureWindow(_cacheWidth, _cacheHeight);
            _cellsByType.Clear();
        }
    }

    /// <summary>Начать проход разрешения метаданных (см. TerrainCellMetadataCache).</summary>
    public void BeginMetadataPass() => _metadataCache.BeginPass();

    public void ClearCaches()
    {
        _metadataCache.Clear();
    }

    public void RefreshTextureMetadata(
        HashSet<CellType> cellTypes,
        IMapDataProvider mapManager,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        _metadataCache.Invalidate(cellTypes);

        // Один проход по окну вместо прохода на каждый приехавший тип.
        // Метаданные типа разрешаются по первой его клетке и дальше отдаются
        // кэшем: внутри прохода тип уже разрешён.
        _refreshEntries.Clear();
        _cellsByType.CollectEntries(cellTypes, _refreshEntries);
        _metadataCache.BeginPass();
        for (int index = 0; index < _refreshEntries.Count; index++)
        {
            (long key, CellType cellType) = _refreshEntries[index];
            int x = TerrainCoordinateKey.UnpackX(key) - _cacheMinX;
            int y = TerrainCoordinateKey.UnpackY(key) - _cacheMinY;
            if ((uint)x >= (uint)_cacheWidth || (uint)y >= (uint)_cacheHeight)
            {
                continue;
            }

            CellMetadata metadata = _metadataCache.GetMetadata(
                cellType, mapManager, textureService, atlases);
            _cellCache[x, y] = _metadataCache.CreateCachedData(cellType, metadata);
        }
    }

    public CachedCellInfo GetCell(int x, int y)
    {
        CachedCellData data = GetCellData(x, y);
        return new CachedCellInfo { Type = data.Type, Properties = data.Properties };
    }

    public CachedCellData GetCellData(int x, int y)
    {
        if (x < 0 || x >= _cacheWidth || y < 0 || y >= _cacheHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Terrain cell cache index ({x}, {y}) is outside {_cacheWidth}x{_cacheHeight}.");
        }

        return _cellCache[x, y];
    }

    public void PopulateFull(int minX, int minY, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        _cacheMinX = minX - 1;
        _cacheMinY = minY - 1;
        _cellsByType.Clear();
        _metadataCache.BeginPass();

        for (int x = 0; x < _cacheWidth; x++)
        {
            int gridX = _cacheMinX + x;
            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;

            for (int y = 0; y < _cacheHeight; y++)
            {
                int unityY = _cacheMinY + y;
                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                if (type == CellType.Unloaded)
                {
                    SetCachedData(x, y, _UnloadedCellData);
                    continue;
                }

                var meta = GetMetadata(type, mm, wtm, atlases);
                SetCachedData(x, y, CreateCachedData(type, meta));
            }
        }

        wtm.RequestTexture(CellType.Empty);
    }

    public void UpdateRegion(int gridMinX, int unityMinY, int width, int height, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null || atlases == null || mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        _metadataCache.BeginPass();
        int startX = Mathf.Clamp(gridMinX - _cacheMinX, 0, _cacheWidth);
        int endX = Mathf.Clamp(gridMinX + width - _cacheMinX, 0, _cacheWidth);
        int startY = Mathf.Clamp(unityMinY - _cacheMinY, 0, _cacheHeight);
        int endY = Mathf.Clamp(unityMinY + height - _cacheMinY, 0, _cacheHeight);

        for (int x = startX; x < endX; x++)
        {
            int gridX = _cacheMinX + x;
            int lastChunkIndex = -1;
            CellType[]? currentChunk = null;

            for (int y = startY; y < endY; y++)
            {
                int unityY = _cacheMinY + y;
                CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref lastChunkIndex, ref currentChunk);

                if (type == CellType.Unloaded)
                {
                    SetCachedData(x, y, _UnloadedCellData);
                    continue;
                }

                var meta = GetMetadata(type, mm, wtm, atlases);
                SetCachedData(x, y, CreateCachedData(type, meta));
            }
        }
    }

    public void ScrollAndFill(int dx, int dy, IWorldDataStorage mapStorage, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (wtm == null)
        {
            throw new ArgumentNullException(nameof(wtm));
        }

        if (atlases == null)
        {
            throw new ArgumentNullException(nameof(atlases));
        }

        if (mm == null || mapStorage == null || !mapStorage.IsReady)
        {
            return;
        }

        int worldWidth = mm.WorldWidth;
        int worldHeight = mm.WorldHeight;
        var layer = mapStorage.CellLayer;
        if (layer == null)
        {
            return;
        }

        _metadataCache.BeginPass();

        // Снимать уехавшие клетки с индекса типов отдельным проходом больше
        // не нужно. Индекс адресует клетку кольцом по размеру окна, и слот
        // уехавшей клетки — это ровно слот той, что встала на её место; Set
        // ниже снимает прежнего жильца сам. Проход же стоил по хеш-операции
        // на клетку полосы, и на догрузке чанка это были миллисекунды за
        // работу, которую тут же делали второй раз.
        _cacheMinX += dx;
        _cacheMinY += dy;

        _cellCache.Scroll(dx, dy);

        int lastChunkIndex = -1;
        CellType[]? currentChunk = null;

        void FillCell(int cx, int cy, ref int chunkIdx, ref CellType[]? chunk)
        {
            int gridX = _cacheMinX + cx;
            int unityY = _cacheMinY + cy;

            CellType type = GetCellType(gridX, unityY, worldWidth, worldHeight, layer, ref chunkIdx, ref chunk);

            if (type == CellType.Unloaded)
            {
                SetCachedData(cx, cy, _UnloadedCellData);
                return;
            }

            var meta = GetMetadata(type, mm, wtm, atlases);
            SetCachedData(cx, cy, CreateCachedData(type, meta));
        }

        // Кайма нулевая: клетка кэша читается сама по себе, соседей здесь
        // никто не смотрит. Полоса по y берёт только ту ширину, которую не
        // накрыла полоса по x, — раньше угол заполнялся дважды.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            _cacheWidth, _cacheHeight, dx, dy);
        for (int x = bands.ColumnBand.xMin; x < bands.ColumnBand.xMax; x++)
        {
            for (int y = bands.ColumnBand.yMin; y < bands.ColumnBand.yMax; y++)
            {
                FillCell(x, y, ref lastChunkIndex, ref currentChunk);
            }
        }

        for (int x = bands.RowBand.xMin; x < bands.RowBand.xMax; x++)
        {
            for (int y = bands.RowBand.yMin; y < bands.RowBand.yMax; y++)
            {
                FillCell(x, y, ref lastChunkIndex, ref currentChunk);
            }
        }

        wtm.RequestTexture(CellType.Empty);
    }

    private CellType GetCellType(int gridX, int unityY, int worldWidth, int worldHeight, IWorldLayer<CellType> layer, ref int lastChunkIndex, ref CellType[]? currentChunk)
    {
        if (unityY >= worldHeight)
        {
            return CellType.Unloaded;
        }

        if (gridX < 0 || gridX >= worldWidth || unityY < 0)
        {
            // The infinite redrock shell is rendered by SurfaceRenderer's
            // boundary shader. It is not terrain data and must never be
            // converted into a server CellType: doing so asks the texture
            // cache for RedRock metadata/animation outside the world and
            // can fail when the server has not configured that cell type.
            return CellType.Unloaded;
        }

        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);
        if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
        {
            return CellType.Unloaded;
        }

        if (chunkIndex != lastChunkIndex)
        {
            ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: true);
            currentChunk = result.Status == ChunkReadStatus.Available
                ? result.Data
                : null;
            lastChunkIndex = chunkIndex;
        }

        return currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
    }

    // Разрешение типа: главный поток. Пишет в кэш и дозаказывает текстуру.
    public CellMetadata GetMetadata(CellType type, IMapDataProvider mm, ITextureService wtm, IReadOnlyList<IAtlasDescriptor> atlases) =>
        _metadataCache.GetMetadata(type, mm, wtm, atlases);

    // Чтение уже разрешённого типа: этим и только этим пользуется сборка
    // клетки, в том числе из рабочих потоков.
    public ITerrainMetadataLookup MetadataLookup => _metadataCache;

    public CachedCellData CreateCachedData(CellType type, CellMetadata meta) =>
        _metadataCache.CreateCachedData(type, meta);

    private void SetCachedData(int x, int y, CachedCellData data)
    {
        // Снимать клетку с прежнего типа отдельным флагом больше не нужно:
        // индекс адресует слот кольцом и сам видит, кто в слоте был.
        _cellsByType.Set(
            TerrainCoordinateKey.Pack(_cacheMinX + x, _cacheMinY + y),
            data.Type);
        _cellCache[x, y] = data;
    }


}
