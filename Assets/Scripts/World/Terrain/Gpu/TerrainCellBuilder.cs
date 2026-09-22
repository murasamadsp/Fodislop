#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellBuilder : IDisposable
{
    // Восемь вершин: фон занимает 0..3, передний план 4..7. Один буфер на
    // клетку, потому что решение «закрывает ли передний план фон целиком»
    // смотрит на оба слоя сразу.
    private sealed class Scratch
    {
        public readonly TerrainVertex[] Vertices = new TerrainVertex[8];

        public Span<TerrainVertex> Background => Vertices.AsSpan(0, 4);

        public Span<TerrainVertex> Foreground => Vertices.AsSpan(4, 4);
    }

    private readonly TerrainCellDataTextures _textures = new();
    private readonly Scratch _mainScratch = new();
    private readonly TerrainDoorOverlayIndex _doors = new();
    private readonly TerrainCellTextureIndex _textureIndex = new();
    private readonly TerrainMetadataWarmup _warmup = new();
    private bool _trackTextureIndex;
    private int _width;
    private int _height;
    private float _cellSize;
    private bool _doorsTouched;

    // Тики, а не миллисекунды: складываются на каждой клетке, переводятся один
    // раз в конце прохода.
    private long _quadTicks;
    private long _packTicks;

    // Captured during the last full production build. This is a diagnostic
    // contract for the runtime integration test: it proves that the scene
    // builder produced foreground geometry with non-canonical corners instead
    // of merely exercising a hand-filled cell-data texture.
    private int _lastFullBuildAnchoredForegroundCellCount;

    internal int LastFullBuildAnchoredForegroundCellCount =>
        Volatile.Read(ref _lastFullBuildAnchoredForegroundCellCount);

    public TerrainCellDataTextures Textures => _textures;

    public bool DoorsTouched => _doorsTouched;

    public bool HasDoors => _doors.HasDoors;

    /// <summary>Сколько стоила последняя сборка, по стадиям.</summary>
    ///
    /// Графа «тексели» в отчёте о провисе оказалась на порядок дороже той же
    /// работы в бенчмарке, а внутри неё четыре разных дела: перенос колец,
    /// снятие уехавших клеток с индекса типов, прогрев метаданных и сама
    /// заливка. Без разбивки следующий шаг опять был бы догадкой.
    public float LastScrollMs { get; private set; }

    public float LastIndexRemoveMs { get; private set; }

    public float LastWarmupMs { get; private set; }

    public float LastFillMs { get; private set; }

    public int LastFilledCells { get; private set; }

    /// <summary>Внутри заливки: сборка двух квадов против упаковки и записи в тексели.</summary>
    ///
    /// Заливка полосы оказалась в тридцать раз дороже той же работы в
    /// бенчмарке, а в ней два разных дела: TerrainQuadBuilder.FillQuad (его
    /// бенчмарк не меряет вообще) и упаковка с записью (её меряет, 43 нс на
    /// клетку). Разделение показывает, какое из двух врёт.
    public float LastQuadMs { get; private set; }

    public float LastPackMs { get; private set; }

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        if (_width == meshWidth && _height == meshHeight && _textures.IsAllocated)
        {
            return;
        }

        _width = meshWidth;
        _height = meshHeight;
        _doors.EnsureSize(meshWidth, meshHeight);
        _textureIndex.EnsureWindow(meshWidth, meshHeight);
        _textureIndex.Clear();
        _textures.EnsureCapacity(meshWidth, meshHeight);
    }

    public void BuildFull(TerrainCellSources sources, int minX, int minY)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        ResetStageTimings();
        _doorsTouched = true;
        Volatile.Write(ref _lastFullBuildAnchoredForegroundCellCount, 0);
        _doors.BeginFullBuild();
        _trackTextureIndex = false;
        _textures.MarkAllDirty();

        // Типы разрешаются здесь и последовательно: FillCell ниже идёт из
        // рабочих потоков и имеет право только читать.
        long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _warmup.WarmRect(sources, 0, _width, 0, _height);
        LastWarmupMs = ElapsedMs(warmStart);

        // Разбивка на квады и упаковку здесь не ведётся: счётчики складываются
        // без синхронизации, а этот путь идёт из рабочих потоков. Общее время
        // и число клеток — ведутся.
        long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Parallel.For(
            0,
            _width,
            static () => new Scratch(),
            (x, _, scratch) =>
            {
                for (int y = 0; y < _height; y++)
                {
                    FillCell(x, y, minX, minY, sources, scratch);
                }

                return scratch;
            },
            static _ => { });
        LastFillMs = ElapsedMs(fillStart);
        LastFilledCells = _width * _height;
        _doors.CompleteFullBuild();
        _trackTextureIndex = true;
        _textureIndex.Rebuild(sources, minX, minY, _width, _height);
    }

    public void ScrollAndBuildBand(TerrainCellSources sources, int minX, int minY, int dx, int dy)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        // Сдвиг во всё окно не оставляет ничего годного, и полосы выродились
        // бы в ту же полную сборку.
        if (Math.Abs(dx) >= _width || Math.Abs(dy) >= _height)
        {
            BuildFull(sources, minX, minY);
            return;
        }

        // Тексели по кольцевому адресу не двигаются: двери переставляет
        // TerrainDoorOverlayIndex вместе с окном.
        ResetStageTimings();
        long scrollStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _doorsTouched = false;
        if (dx != 0 || dy != 0)
        {
            _doorsTouched = _doors.Scroll(dx, dy);
            LastScrollMs = ElapsedMs(scrollStart);

            // Уехавшие клетки с индекса типов не снимаются: слот кольца
            // передаётся приехавшей клетке, и UpdateCell ниже снимает
            // прежнего жильца сам. Полоса заливки накрывает каждый такой
            // слот, поэтому отдельный проход был чистым дублем — и стоил
            // хеш-операции на каждую клетку полосы.
        }

        // Кайма в одну клетку: тексель клетки несёт маски соседства, и у
        // клетки на старой границе сосед снаружи только что появился.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            _width, _height, dx, dy, neighbourMargin: 1);

        TerrainScrollBands entered = TerrainScrollBands.Resolve(_width, _height, dx, dy);
        _doors.ClearBand(entered.ColumnBand);
        _doors.ClearBand(entered.RowBand);
        FillBand(bands.ColumnBand, minX, minY, sources);
        FillBand(bands.RowBand, minX, minY, sources);
    }

    public void BuildRegion(
        TerrainCellSources sources,
        int minX,
        int minY,
        int startX,
        int startY,
        int countX,
        int countY)
    {
        ResetStageTimings();
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        FillRect(
            Mathf.Clamp(startX, 0, _width),
            Mathf.Clamp(startX + countX, 0, _width),
            Mathf.Clamp(startY, 0, _height),
            Mathf.Clamp(startY + countY, 0, _height),
            minX,
            minY,
            sources);
    }

    public void BuildTextureCells(HashSet<CellType> cellTypes, TerrainCellSources sources, int minX, int minY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        ResetStageTimings();

        long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _warmup.WarmRect(sources, 0, _width, 0, _height);
        LastWarmupMs = ElapsedMs(warmStart);

        // Сбор квадов по типам занимает отдельную графу: он идёт по обратному
        // индексу, а не по окну, и его цена растёт с числом приехавших типов.
        long collectStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _textureIndex.CollectRefreshQuads(cellTypes, minX, minY, _width, _height);
        LastIndexRemoveMs = ElapsedMs(collectStart);
        List<int> refreshQuads = _textureIndex.TextureRefreshQuads;
        bool trackTextureIndex = _trackTextureIndex;
        _trackTextureIndex = false;
        try
        {
            long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _quadTicks = 0;
            _packTicks = 0;
            for (int index = 0; index < refreshQuads.Count; index++)
            {
                int quad = refreshQuads[index];
                int x = quad / _height;
                int y = quad % _height;
                _doorsTouched |= FillCell(x, y, minX, minY, sources, _mainScratch);
            }

            LastFillMs = ElapsedMs(fillStart);
            LastQuadMs = TicksToMs(_quadTicks);
            LastPackMs = TicksToMs(_packTicks);
            LastFilledCells = refreshQuads.Count;
        }
        finally
        {
            _trackTextureIndex = trackTextureIndex;
        }

        MarkTextureRefreshRuns(refreshQuads, minX, minY);
    }

    private void MarkTextureRefreshRuns(List<int> refreshQuads, int minX, int minY)
    {
        int index = 0;
        while (index < refreshQuads.Count)
        {
            int firstQuad = refreshQuads[index];
            int x = firstQuad / _height;
            int firstY = firstQuad % _height;
            int lastY = firstY;
            index++;

            while (index < refreshQuads.Count)
            {
                int nextQuad = refreshQuads[index];
                if (nextQuad / _height != x || nextQuad % _height != lastY + 1)
                {
                    break;
                }

                lastY++;
                index++;
            }

            _textures.MarkCells(
                TerrainCellDataTextures.Ring(minX + x, _width),
                TerrainCellDataTextures.Ring(minY + firstY, _height),
                1,
                lastY - firstY + 1);
        }
    }


    // Вершины накладки дверей: клеток с дверью мало, поэтому их квады
    // собираются заново по требованию, а не хранятся для всей сетки.
    public void BuildDoorOverlay(
        TerrainCellSources sources,
        int minX,
        int minY,
        List<TerrainVertex> vertices,
        List<int>[] indicesPerAtlas) =>
        _doors.BuildOverlay(
            sources,
            minX,
            minY,
            _cellSize,
            _mainScratch.Foreground,
            vertices,
            indicesPerAtlas);

    // Выгрузка cell-data на GPU и адрес окна для шейдера. При scroll
    // переписываются только новые клетки; полный upload остаётся для первого
    // build и resize.
    public void Commit(int originX, int originY)
    {
        _textures.Apply();
        _textures.BindGlobals(_cellSize, originX, originY);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _width = 0;
        _height = 0;
    }

    // Квад переднего плана даёт альфу 1 на каждом пикселе клетки, только если
    // у него есть текстура, она непрозрачна вся, цвет без прозрачности, нет
    // скругления контура и шейдер не выводит квад прозрачным.
    private static bool ForegroundCoversCell(int x, int y, int foreground, Scratch scratch, TerrainCellSources sources)
    {
        if (foreground < 0 || foreground >= sources.Atlases.Count)
        {
            return false;
        }

        ref TerrainVertex vertex = ref scratch.Vertices[4];
        const int RoundableFlag = 2;
        bool hasTexture = vertex.UV1z != 0 && Mathf.HalfToFloat(vertex.UV1z) > 0.0001f;
        bool roundable = (Mathf.RoundToInt(vertex.UV6.z) & RoundableFlag) != 0;
        if (!hasTexture || roundable || vertex.Color.a < 255)
        {
            return false;
        }

        // A displaced foreground quad no longer covers the whole cell.  The
        // background must remain drawable behind the exposed edge; otherwise
        // the background is culled as a full rectangle and the quantized
        // silhouette reveals the cleared render target as a black seam.
        if (vertex.UV5x != 0)
        {
            return false;
        }

        CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
        return sources.Atlases[foreground].IsFullyOpaque(foregroundType);
    }

    private bool CanBuild(TerrainCellSources sources) =>
        _textures.IsAllocated && sources.Atlases != null && sources.Atlases.Count > 0;

    // Графы отчёта обнуляются на входе в КАЖДЫЙ путь сборки. Пока это делали
    // только сдвиг и перечитывание текстур, отчёт о полной сборке печатал
    // числа прошлого кадра — и они выглядели как измерение, а не как мусор.
    private void ResetStageTimings()
    {
        LastScrollMs = 0f;
        LastIndexRemoveMs = 0f;
        LastWarmupMs = 0f;
        LastFillMs = 0f;
        LastFilledCells = 0;
        LastQuadMs = 0f;
        LastPackMs = 0f;
    }

    private static float TicksToMs(long ticks) =>
        (float)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);

    private void FillBand(RectInt band, int minX, int minY, TerrainCellSources sources) =>
        FillRect(band.xMin, band.xMax, band.yMin, band.yMax, minX, minY, sources);

    private void FillRect(int startX, int endX, int startY, int endY, int minX, int minY, TerrainCellSources sources)
    {
        if (endX <= startX || endY <= startY)
        {
            return;
        }

        // Строки полосы независимы, и распараллелить её можно так же, как полную
        // сборку (BuildFull идёт через Parallel.For по столбцам). Не сделано
        // сознательно: счётчики стадий (_quadTicks, LastFillMs) складываются без
        // синхронизации, а главное — это оптимизация на догадке, пока нет замера
        // F1 frametime, который показал бы, что полоса вообще узкое место.

        long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _warmup.WarmRect(sources, startX, endX, startY, endY);
        LastWarmupMs += ElapsedMs(warmStart);

        long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _quadTicks = 0;
        _packTicks = 0;
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                _doorsTouched |= FillCell(x, y, minX, minY, sources, _mainScratch);
            }
        }

        LastFillMs += ElapsedMs(fillStart);
        LastQuadMs += TicksToMs(_quadTicks);
        LastPackMs += TicksToMs(_packTicks);
        LastFilledCells += (endX - startX) * (endY - startY);

        _textures.MarkCells(
            TerrainCellDataTextures.Ring(minX + startX, _width),
            TerrainCellDataTextures.Ring(minY + startY, _height),
            endX - startX,
            endY - startY);
    }

    // Возвращает признак «двери задеты» вместо записи в общее поле: полная
    // сборка зовёт FillCell из Parallel.For, и такая запись была гонкой.
    private bool FillCell(int x, int y, int minX, int minY, TerrainCellSources sources, Scratch scratch)
    {
        int gridX = minX + x;
        int unityY = minY + y;
        var site = new TerrainQuadSite(x, y, gridX, unityY, _cellSize);

        long quadStart = System.Diagnostics.Stopwatch.GetTimestamp();
        int background = TerrainQuadBuilder
            .FillQuad(sources, site, TerrainQuadLayer.Background, scratch.Background)
            .AtlasIndex;
        TerrainQuadResult foregroundQuad = TerrainQuadBuilder.FillQuad(
            sources, site, TerrainQuadLayer.Foreground, scratch.Foreground);
        int foreground = foregroundQuad.AtlasIndex;
        _quadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - quadStart;

        if (foregroundQuad.HasAtlas && scratch.Vertices[4].UV5x != 0)
        {
            Interlocked.Increment(ref _lastFullBuildAnchoredForegroundCellCount);
        }

        bool doorsChanged = _doors.RecordCell(
            x, y, foreground, foregroundQuad.IsDoor, scratch.Foreground);
        if (_trackTextureIndex)
        {
            _textureIndex.UpdateCell(
                gridX,
                unityY,
                sources.FloodFill.Buffer[x, y],
                sources.CellCache.GetCellData(x + 1, y + 1).Type);
        }

        long packStart = System.Diagnostics.Stopwatch.GetTimestamp();
        int ringX = TerrainCellDataTextures.Ring(gridX, _width);
        int ringY = TerrainCellDataTextures.Ring(unityY, _height);
        TerrainCellTexels backgroundTexels = TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(0, 4), background);
        if (background >= 0 && ForegroundCoversCell(x, y, foreground, scratch, sources))
        {
            // Сплошной передний план закрывает фон целиком: смешивание
            // полностью непрозрачного пикселя не оставляет от фона ни цвета,
            // ни света, ни тени. Квад фона отбрасывается в вершинном шейдере.
            Color32 meta = backgroundTexels.Meta;
            backgroundTexels = backgroundTexels with
            {
                Meta = new Color32(meta.r, meta.g, byte.MaxValue, meta.a),
            };
        }

        _textures.SetCell(ringX, ringY, TerrainCellDataPacker.BackgroundLayer, backgroundTexels);
        _textures.SetCell(
            ringX, ringY, TerrainCellDataPacker.ForegroundLayer,
            TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(4, 4), foreground));
        _packTicks += System.Diagnostics.Stopwatch.GetTimestamp() - packStart;
        return doorsChanged;
    }
}
