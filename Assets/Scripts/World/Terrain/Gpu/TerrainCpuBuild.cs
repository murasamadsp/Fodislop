#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Шаг фоновой сборки террейна: что главный поток уже положил в кэш клеток и
/// что рабочему потоку осталось досчитать.
/// </summary>
///
/// Кэш клеток заполняется на главном потоке — это единственное место, где
/// читается живое хранилище мира и разрешаются типы. После этого кэш,
/// предрасчёт, заливка фона и тексели принадлежат рабочему потоку до
/// публикации. Снимок кэша не копируется: владение передаётся целиком, а
/// главный поток до публикации к этим структурам не прикасается.
internal sealed class TerrainCpuBuildRequest
{
    public TerrainCpuBuildRequest(
        Vector2Int origin,
        Vector2Int size,
        bool cacheScrolled,
        Vector2Int scrollDelta,
        bool buildFull,
        IReadOnlyList<IAtlasDescriptor> atlases,
        int worldWidth,
        int worldHeight,
        RectInt[] dirtyRects,
        HashSet<CellType> textureTypes,
        ulong contentRevision,
        long worldGeneration)
    {
        Origin = origin;
        Size = size;
        CacheScrolled = cacheScrolled;
        ScrollDelta = scrollDelta;
        BuildFull = buildFull;
        Atlases = atlases;
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        DirtyRects = dirtyRects;
        TextureTypes = textureTypes;
        ContentRevision = contentRevision;
        WorldGeneration = worldGeneration;
    }

    public Vector2Int Origin { get; }

    public Vector2Int Size { get; }

    /// <summary>Кэш перенёс перекрытие; предрасчёт и заливка идут приращением.</summary>
    public bool CacheScrolled { get; }

    public Vector2Int ScrollDelta { get; }

    /// <summary>Тексели собираются целиком: перекрытие переносить нельзя.</summary>
    public bool BuildFull { get; }

    /// <summary>Числовые снимки атласов: живые атласы меняются на главном потоке.</summary>
    public IReadOnlyList<IAtlasDescriptor> Atlases { get; }

    public int WorldWidth { get; }

    public int WorldHeight { get; }

    /// <summary>Изменённые клетки в координатах Unity, уже перечитанные в кэш.</summary>
    public RectInt[] DirtyRects { get; }

    /// <summary>Типы, чьи метаданные обновились после приезда текстуры.</summary>
    public HashSet<CellType> TextureTypes { get; }

    public ulong ContentRevision { get; }

    public long WorldGeneration { get; }
}

/// <summary>Итог шага: что изменилось и сколько это стоило рабочему потоку.</summary>
internal sealed class TerrainCpuBuildResult
{
    public bool DoorsTouched { get; set; }

    /// <summary>Раскладка снятых клеток в кольцо кэша.</summary>
    public float CacheMs { get; set; }

    public float PrecalculateMs { get; set; }

    public float FloodFillMs { get; set; }

    public float MeshMs { get; set; }

    public float ElapsedMs { get; set; }

    // Разбивка текселей по стадиям сборщика, сложенная по всем его вызовам
    // шага: полоса и каждая заплатка сбрасывают счётчики сборщика заново.
    public float ScrollMs { get; set; }

    public float WarmupMs { get; set; }

    public float FillMs { get; set; }

    public int FilledCells { get; set; }

    public float QuadMs { get; set; }

    public float PackMs { get; set; }

    public void AddBuilderStages(TerrainCellBuilder builder)
    {
        ScrollMs += builder.LastScrollMs;
        WarmupMs += builder.LastWarmupMs;
        FillMs += builder.LastFillMs;
        FilledCells += builder.LastFilledCells;
        QuadMs += builder.LastQuadMs;
        PackMs += builder.LastPackMs;
    }
}
