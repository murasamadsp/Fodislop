#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Источники данных и размер окна для одного прохода сборки.</summary>
public readonly record struct TerrainBuildContext(
    IWorldDataStorage Storage,
    IMapDataProvider MapData,
    ITextureService TextureService,
    IReadOnlyList<IAtlasDescriptor> Atlases,
    IFrameTelemetry Telemetry,
    int MeshWidth,
    int MeshHeight);

/// <summary>
/// Путь клетки от данных мира до текселя: кэш → предрасчёт → заливка фона →
/// тексели.
/// </summary>
///
/// Четыре стадии всегда идут вместе и в этом порядке: маски соседства читают
/// кэш, заливка читает кэш, тексель читает и маски, и заливку. Поэтому они
/// живут одним типом, а не четырьмя полями рендерера, и каждая стадия имеет
/// ровно три входа — полный проход, сдвиг окна и заплатка по прямоугольнику.
public sealed class TerrainBuildPipeline : IDisposable
{
    private static readonly ProfilerMarker _CacheMarker = new("Kern.Terrain.Cache");
    private static readonly ProfilerMarker _PrecalculateMarker = new("Kern.Terrain.Precalculate");
    private static readonly ProfilerMarker _FloodFillMarker = new("Kern.World.Terrain.BackgroundFloodFill");
    private static readonly ProfilerMarker _MeshBuildMarker = new("Kern.Terrain.MeshBuild");
    private static readonly ProfilerMarker _MeshUploadMarker = new("Kern.Terrain.MeshUpload");

    private readonly TerrainCellCache _cellCache = new();
    private readonly TerrainPrecalculator _precalc = new();
    private readonly BackgroundFloodFill _floodFill = new();
    private readonly TerrainCellBuilder _cellBuilder = new();

    public TerrainCellCache CellCache => _cellCache;

    public TerrainCellBuilder CellBuilder => _cellBuilder;

    public bool EnableDistortion
    {
        get => _precalc.EnableDistortion;
        set => _precalc.EnableDistortion = value;
    }

    /// <summary>Последний проход по окну перенёс перекрытие вместо полной сборки.</summary>
    public bool LastBuildScrolled { get; private set; }

    /// <summary>
    /// На сколько клеток переехало окно в последнем проходе. Накладка дверей
    /// компенсирует этим сдвиг своего родителя, когда состав дверей не менялся.
    /// </summary>
    public Vector2Int LastScrollDelta { get; private set; }

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellCache.EnsureCapacity(meshWidth, meshHeight);
        _precalc.EnsureCapacity(meshWidth, meshHeight);
        _cellBuilder.EnsureCapacity(meshWidth, meshHeight, cellSize);
        _floodFill.Allocate(meshWidth, meshHeight);
    }

    public TerrainCellSources CreateSources(in TerrainBuildContext context) =>
        new(
            _cellCache,
            _precalc,
            _floodFill,
            context.MapData.WorldWidth,
            context.MapData.WorldHeight,
            context.Atlases,
            context.MapData,
            context.TextureService);

    /// <summary>
    /// Собрать окно с началом (minX, minY).
    /// </summary>
    ///
    /// <param name="forceFull">
    /// Перекрытие переносить нельзя: содержимое окна изменилось целиком.
    /// </param>
    /// <param name="atlasSetChanged">
    /// Набор атласов сменился, и индексы атласов в уже записанных текселях
    /// посчитаны по старому набору — переносить их нельзя.
    /// </param>
    public void BuildWindow(
        in TerrainBuildContext context,
        int minX,
        int minY,
        bool forceFull,
        bool atlasSetChanged)
    {
        IFrameTelemetry telemetry = context.Telemetry;
        int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
        int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
        bool canScrollCache =
            !forceFull &&
            _cellCache.CacheMinX != int.MinValue &&
            Math.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
            Math.Abs(cacheDeltaY) < _cellCache.CacheHeight;
        LastBuildScrolled = canScrollCache;
        LastScrollDelta = canScrollCache
            ? new Vector2Int(cacheDeltaX, cacheDeltaY)
            : Vector2Int.zero;
        telemetry.TerrainRebuildCount++;

        long cacheStart = Stopwatch.GetTimestamp();
        using (_CacheMarker.Auto())
        {
            if (canScrollCache)
            {
                _cellCache.ScrollAndFill(
                    cacheDeltaX, cacheDeltaY, context.Storage, context.MapData,
                    context.TextureService, context.Atlases);
            }
            else
            {
                telemetry.TerrainFullPopulateCount++;
                _cellCache.PopulateFull(
                    minX, minY, context.Storage, context.MapData,
                    context.TextureService, context.Atlases);
            }
        }

        telemetry.TerrainCacheTimeMs = ElapsedMs(cacheStart);

        using (_PrecalculateMarker.Auto())
        {
            if (canScrollCache)
            {
                _precalc.PrecalculateIncremental(
                    _cellCache, context.MeshWidth, context.MeshHeight,
                    cacheDeltaX, cacheDeltaY,
                    context.MapData.WorldWidth, context.MapData.WorldHeight);
            }
            else
            {
                _precalc.PrecalculateFull(
                    _cellCache, context.MeshWidth, context.MeshHeight,
                    context.MapData.WorldWidth, context.MapData.WorldHeight);
            }
        }

        long floodStart = Stopwatch.GetTimestamp();
        using (_FloodFillMarker.Auto())
        {
            // Тем же сдвигом, что кэш и предрасчёт выше: иначе на
            // каждом переходе через границу региона заливка одна
            // платила по площади за то, что сдвинулось на кайму.
            if (canScrollCache)
            {
                _floodFill.ComputeScrolled(cacheDeltaX, cacheDeltaY, _cellCache);
            }
            else
            {
                _floodFill.ComputeFull(_cellCache);
            }
        }

        telemetry.TerrainFloodFillTimeMs = ElapsedMs(floodStart);

        long meshStart = Stopwatch.GetTimestamp();
        TerrainCellSources sources = CreateSources(context);
        using (_MeshBuildMarker.Auto())
        {
            // Тексели лежат по кольцевому адресу и при сдвиге не
            // двигаются: собирается только вошедшая полоса. Полная
            // сборка остаётся там, где переносить нечего, и при смене
            // набора атласов — индексы атласов в текселях считаны по
            // старому набору.
            if (canScrollCache && !atlasSetChanged)
            {
                _cellBuilder.ScrollAndBuildBand(sources, minX, minY, cacheDeltaX, cacheDeltaY);
            }
            else
            {
                _cellBuilder.BuildFull(sources, minX, minY);
            }
        }

        telemetry.TerrainMeshTimeMs = ElapsedMs(meshStart);
    }

    /// <summary>Пересчитать изменённые прямоугольники в координатах окна.</summary>
    public bool PatchRegions(
        in TerrainBuildContext context,
        int minX,
        int minY,
        DirtyRectSet dirtyRects)
    {
        context.Telemetry.TerrainDirtyPatchCount++;

        TerrainCellSources sources = CreateSources(context);
        bool doorsTouched = false;
        for (int index = 0; index < dirtyRects.Count; index++)
        {
            RectInt rect = dirtyRects[index];

            int dirtyMinX = rect.xMin - 1;
            int dirtyMaxX = rect.xMax + 1;
            int dirtyMinY = rect.yMin - 1;
            int dirtyMaxY = rect.yMax + 1;

            int localStartX = dirtyMinX - minX;
            int localStartY = dirtyMinY - minY;
            int countX = dirtyMaxX - dirtyMinX;
            int countY = dirtyMaxY - dirtyMinY;

            _cellCache.UpdateRegion(
                dirtyMinX, dirtyMinY, countX, countY, context.Storage, context.MapData,
                context.TextureService, context.Atlases);
            _precalc.PrecalculateRegion(
                _cellCache, context.MeshWidth, context.MeshHeight,
                localStartX, localStartY, countX, countY,
                context.MapData.WorldWidth, context.MapData.WorldHeight);
            _floodFill.UpdateLocalRegion(localStartX, localStartY, countX, countY, _cellCache);
            _cellBuilder.BuildRegion(
                sources, minX, minY, localStartX, localStartY, countX, countY);
            doorsTouched |= _cellBuilder.DoorsTouched;
        }

        return doorsTouched;
    }

    /// <summary>Перечитать клетки типов, у которых только что приехала текстура.</summary>
    public void RefreshTextureCells(
        in TerrainBuildContext context,
        HashSet<CellType> cellTypes,
        int minX,
        int minY)
    {
        _cellCache.RefreshTextureMetadata(
            cellTypes, context.MapData, context.TextureService, context.Atlases);
        _cellBuilder.BuildTextureCells(cellTypes, CreateSources(context), minX, minY);
    }

    /// <summary>
    /// Одна выгрузка текселей за кадр, после сборки или заплатки. Начало окна
    /// публикуется вместе с ними: шейдер берёт по нему кольцевой адрес.
    /// </summary>
    public float Commit(int originX, int originY)
    {
        long start = Stopwatch.GetTimestamp();
        using (_MeshUploadMarker.Auto())
        {
            _cellBuilder.Commit(originX, originY);
        }

        return ElapsedMs(start);
    }

    public void Dispose() => _cellBuilder.Dispose();

    private static float ElapsedMs(long startTimestamp) =>
        (float)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);
}
