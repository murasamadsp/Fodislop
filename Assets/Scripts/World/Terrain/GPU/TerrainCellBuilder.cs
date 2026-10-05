#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellBuilder : IDisposable
{
    private readonly TerrainCellBuffers _buffers = new();
    private readonly TerrainDoorOverlayIndex _doors = new();
    private readonly TerrainCellFillExecutor _fillExecutor;
    private int _width;
    private int _height;
    private float _cellSize;
    private int _worldWidth;
    private int _worldHeight;
    private int _distortionMode;
    private bool _doorsTouched;

    internal int LastFullBuildAnchoredForegroundCellCount =>
        _fillExecutor.LastFullBuildAnchoredForegroundCellCount;

    public TerrainCellBuilder()
    {
        _fillExecutor = new TerrainCellFillExecutor(_buffers, _doors);
    }

    public TerrainCellBuffers Buffers => _buffers;

    public float CellSize => _cellSize;

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


    public float LastFillMs => _fillExecutor.LastFillMs;

    public int LastFilledCells => _fillExecutor.LastFilledCells;

    /// <summary>Сумма времени классификации клеток (дверь, смещение) по рабочим потокам, не длительность кадра.</summary>
    ///
    /// Отдельно от упаковки: так видно, какое из двух дел дорожает.
    public float LastQuadMs => _fillExecutor.LastQuadMs;

    /// <summary>Сумма времени упаковки по рабочим потокам; может превышать LastFillMs.</summary>
    public float LastPackMs => _fillExecutor.LastPackMs;

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        _fillExecutor.Configure(meshWidth, meshHeight, cellSize);
        if (_width == meshWidth && _height == meshHeight && _buffers.IsAllocated)
        {
            return;
        }

        _width = meshWidth;
        _height = meshHeight;
        _doors.EnsureSize(meshWidth, meshHeight);
        _buffers.EnsureCapacity(meshWidth, meshHeight);
    }

    public void BuildFull(TerrainCellSources sources, int minX, int minY)
    {
        BuildFull(sources, minX, minY, CancellationToken.None);
    }

    public void BuildFull(
        TerrainCellSources sources,
        int minX,
        int minY,
        CancellationToken cancellationToken)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        ResetStageTimings();
        _doorsTouched = true;
        _doors.BeginFullBuild();
        _buffers.MarkAllDirty();
        _fillExecutor.FillFull(sources, minX, minY, cancellationToken);
        _doors.CompleteFullBuild();
        FinishBuild(sources, minX, minY);
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
        FinishBuild(sources, minX, minY);
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
        FinishBuild(sources, minX, minY);
    }

    // Приехала текстура типа. В клетке нет ничего, что от неё зависит: вид
    // типа уходит строкой таблицы (FinishBuild), шейдер подхватывает её во
    // всех клетках сразу, включая накладку дверей.
    internal void BuildTextureCells(
        in TerrainCellTypeSet cellTypes,
        TerrainCellSources sources,
        int minX,
        int minY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources) || cellTypes.IsEmpty)
        {
            return;
        }

        ResetStageTimings();
        FinishBuild(sources, minX, minY);
    }

    /// <summary>Квады дверей окна по возрастанию — для меша накладки.</summary>
    public void CopyDoorQuads(List<int> target) => _doors.CopyQuads(target);

    // Выгрузка cell-data на GPU и адрес окна для шейдера. При scroll
    // переписываются только новые клетки; полный upload остаётся для первого
    // build и resize.
    public void Commit(int originX, int originY)
    {
        _buffers.Apply();
        _buffers.BindGlobals(_cellSize, originX, originY, _worldWidth, _worldHeight, _distortionMode);
    }

    public void Dispose()
    {
        _buffers.Dispose();
        _width = 0;
        _height = 0;
    }

    private bool CanBuild(TerrainCellSources sources)
    {
        if (!_buffers.IsAllocated || sources.Atlases == null || sources.Atlases.Count == 0)
        {
            return false;
        }

        _worldWidth = sources.WorldWidth;
        _worldHeight = sources.WorldHeight;
        _distortionMode = TerrainCellData.DistortionMode(sources.Distortion);
        return true;
    }

    private void FinishBuild(TerrainCellSources sources, int minX, int minY)
    {
        RefreshTypes(sources);
        RefreshMargin(sources, minX, minY);
    }

    // Кайма кольца: тип и узел клеток вокруг окна. Её читают только соседи
    // краевых клеток, поэтому пишется вся (2·(w+h)+4 клеток), а на выгрузку
    // помечается четырьмя полосами и только если что-то поменялось.
    private void RefreshMargin(TerrainCellSources sources, int minX, int minY)
    {
        bool changed = false;
        for (int x = -1; x <= _width; x++)
        {
            changed |= RefreshMarginCell(sources, minX, minY, x, -1);
            changed |= RefreshMarginCell(sources, minX, minY, x, _height);
        }

        for (int y = 0; y < _height; y++)
        {
            changed |= RefreshMarginCell(sources, minX, minY, -1, y);
            changed |= RefreshMarginCell(sources, minX, minY, _width, y);
        }

        if (changed)
        {
            _buffers.MarkCells(minX - 1, minY - 1, _width + 2, 1);
            _buffers.MarkCells(minX - 1, minY + _height, _width + 2, 1);
            _buffers.MarkCells(minX - 1, minY, 1, _height);
            _buffers.MarkCells(minX + _width, minY, 1, _height);
        }
    }

    private bool RefreshMarginCell(TerrainCellSources sources, int minX, int minY, int x, int y)
    {
        TerrainCell cell = TerrainCellPacker.PackMargin(sources, x, y);
        if (_buffers.GetCell(minX + x, minY + y) == cell)
        {
            return false;
        }

        _buffers.SetCell(minX + x, minY + y, cell);
        return true;
    }

    // Таблица типов обновляется в конце каждой сборки, после прогрева
    // метаданных: строка типа — то же, что сборка квада взяла из его конфига
    // (ResolveTypeSurface), и вид типа, приехавший с текстурой, доходит до
    // всех его клеток в той же публикации. Тип без разрешённой метаданности
    // оставляет прежнюю строку: перезапись нулём стёрла бы вид, который ещё
    // может быть на экране.
    private void RefreshTypes(TerrainCellSources sources)
    {
        for (int index = 0; index < TerrainCellData.TypeCount; index++)
        {
            var type = (CellType)index;
            if (sources.MetadataLookup.TryGet(type, out CellMetadata metadata))
            {
                _buffers.SetType(
                    type,
                    TerrainCellData.PackType(
                        TerrainCellPacker.ResolveTypeSurface(type, in metadata, sources.Atlases)));
            }
        }
    }

    // Графы отчёта обнуляются на входе в КАЖДЫЙ путь сборки. Пока это делали
    // только сдвиг и перечитывание текстур, отчёт о полной сборке печатал
    // числа прошлого кадра — и они выглядели как измерение, а не как мусор.
    private void ResetStageTimings()
    {
        LastScrollMs = 0f;
        LastIndexRemoveMs = 0f;
        _fillExecutor.ResetStageTimings();
    }

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);

    private void FillBand(RectInt band, int minX, int minY, TerrainCellSources sources) =>
        FillRect(band.xMin, band.xMax, band.yMin, band.yMax, minX, minY, sources);

    private void FillRect(int startX, int endX, int startY, int endY, int minX, int minY, TerrainCellSources sources)
    {
        _doorsTouched |= _fillExecutor.FillRect(
            startX,
            endX,
            startY,
            endY,
            minX,
            minY,
            sources);
    }
}
