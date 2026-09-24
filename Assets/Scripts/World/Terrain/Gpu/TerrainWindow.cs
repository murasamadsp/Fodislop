#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using Kern.World.Streaming;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public enum TerrainBuildState
{
    WaitingForData,
    CpuPreparing,
    WaitingForPublication,
    Published,
    Canceled,
    Error,
}

/// <summary>
/// Окно террейна: где оно стоит, что в нём просрочено и как оно доводится до
/// текселей на GPU.
/// </summary>
///
/// У окна три состояния, и все три меняются здесь: начало (куда переехала
/// сетка), просроченные клетки (что изменил мир) и признак «тексели выгружены».
/// Вместе они решают единственный вопрос кадра — собрать окно целиком,
/// сдвинуть с полосой, заплатать изменённое или не делать ничего.
///
/// Сборка идёт в фоне шагами. Шаг — приращение к состоянию конвейера:
/// главный поток перечитывает в кэш вошедшие и изменённые клетки, рабочий
/// поток досчитывает предрасчёт, заливку и тексели, главный поток публикует
/// тексели, начало окна и двери в одном кадре. Пока шаг идёт, конвейер
/// принадлежит рабочему потоку, и следующего шага нет: всё, что мир изменил
/// за это время, копится и становится следующим шагом. Готовый шаг поэтому
/// никогда не выбрасывается из-за движения камеры.
public sealed class TerrainWindow : IDisposable
{
    private static readonly RebuildLedger.Entry _RebuildResize = RebuildLedger.Register("Террейн · полная: смена размера сетки");
    private static readonly RebuildLedger.Entry _RebuildGridMove = RebuildLedger.Register("Террейн · полная: сдвиг сетки");
    private static readonly RebuildLedger.Entry _RebuildRefresh = RebuildLedger.Register("Террейн · полная: флаг обновления");
    private static readonly RebuildLedger.Entry _RebuildPatch = RebuildLedger.Register("Террейн · частичная: изменённые клетки");

    private readonly TerrainBuildDriver _driver = new();
    private readonly TerrainDirtyTracker _dirty = new();
    private HashSet<CellType> _pendingTextureCellTypes = [];
    private HashSet<CellType> _buildTextureCellTypes = [];
    private readonly DirtyRectSet _publishedChangedRegions = new();
    private List<RectInt> _changedRegions = [];
    private List<RectInt> _buildChangedRegions = [];

    // Меш идентификаторов всей сетки: поле материалов рисуется целиком, без
    // смещения показа, поэтому у него собственный меш на весь прямоугольник.
    private readonly TerrainCellIDMesh _cellIDMesh = new();
    private readonly TerrainBuildScheduler<TerrainCpuBuildRequest, TerrainCpuBuildResult> _builds;

    private Transform? _transform;
    private float _cellSize = 1f;
    private bool _cellTexturesDirty;
    private bool _wasCpuMeshRebuildBypassed;
    private long _worldGeneration;
    private long _buildStartTimestamp;
    private long _oldestChangeTimestamp;
    private long _buildOldestChangeTimestamp;
    private bool _buildRefreshesTextures;
    private bool? _requestedDistortion;
    private TerrainDistortionStyle? _requestedDistortionStyle;
    private bool _rebuildAllCells;
    private TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult>? _heldCompletion;
    private float _preparationLatencySeconds = StreamingPolicy.DefaultPreparationLatencySeconds;

    public TerrainWindow()
    {
        _builds = new(_driver.Execute);
    }

    public TerrainBuildDriver Driver => _driver;

    /// <summary>Изменения мира, ещё не взятые в шаг.</summary>
    public TerrainDirtyTracker Dirty => _dirty;

    public HashSet<CellType> PendingTextureCellTypes => _pendingTextureCellTypes;

    public Mesh? CellIDMesh => _cellIDMesh.Mesh;

    /// <summary>Начало опубликованного окна: его видит шейдер.</summary>
    public Vector2Int Origin { get; private set; } = new(int.MinValue, int.MinValue);

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsInitialized { get; private set; }

    /// <summary>На GPU лежит согласованная опубликованная версия текущего мира.</summary>
    public bool CellsCommitted { get; private set; }

    public ulong PublishedContentRevision { get; private set; }

    /// <summary>Перекрытие переносить нельзя: содержимое окна изменилось целиком.</summary>
    public bool NeedsRefresh { get; set; }

    public bool HasOrigin => Origin.x != int.MinValue;

    public bool HasCpuBuildInFlight => _builds.IsBusy;

    /// <summary>Приехавшие текстуры уже взяты в шаг, но ещё не на экране.</summary>
    public bool HasUnpublishedTextureRefresh => _buildRefreshesTextures;

    public TerrainBuildState BuildState { get; private set; } = TerrainBuildState.WaitingForData;

    /// <summary>Сглаженная длительность шага от постановки до публикации, в секундах.</summary>
    public float EstimatedPreparationSeconds => _preparationLatencySeconds;

    /// <summary>Начало, в координатах которого копятся изменения мира.</summary>
    ///
    /// Во время шага конвейер уже стоит в его начале: вошедшая полоса прочитана
    /// из хранилища в момент постановки, и изменение внутри неё обязано попасть
    /// в следующий шаг, даже если опубликованное окно его ещё не накрывает.
    private Vector2Int BuildOrigin => _builds.ActiveRequest?.Origin ?? HeldOrigin ?? Origin;

    /// <summary>
    /// Готовый шаг не публикуется, пока идёт переход вида (телепорт): экран
    /// показывает прежнее окно, а шаг ждёт кадра, в котором камера встанет
    /// на место назначения.
    /// </summary>
    public bool HoldPublication { get; set; }

    /// <summary>Начало окна, собранного и ждущего публикации.</summary>
    public Vector2Int? HeldOrigin => _heldCompletion?.Request.Origin;

    /// <summary>
    /// Начало, которое окажется на экране после публикации всего готового:
    /// удержанного шага, если он есть, иначе опубликованного окна.
    /// </summary>
    public Vector2Int ProspectiveOrigin => HeldOrigin ?? Origin;

    public void Attach(
        Transform transform,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize)
    {
        _transform = transform;
        _cellSize = cellSize;
        _driver.Attach(transform, sceneObjects, sortingLayerName, doorOverlaySortingOrder, cellSize);
    }

    /// <summary>
    /// Принять размер сетки. Смена размера роняет начало окна: кольцевые адреса
    /// текселей считаны по старому размеру и переносу не подлежат.
    /// </summary>
    ///
    /// Во время шага массивы конвейера принадлежат рабочему потоку: шаг
    /// отменяется, а размер применяется в кадре после его завершения —
    /// планировщик кадра продолжит просить новый размер.
    public void ApplyDimensions(Vector2Int size, bool dimensionsChanged)
    {
        if (!dimensionsChanged && IsInitialized)
        {
            return;
        }

        if (_builds.IsBusy)
        {
            _builds.Cancel();
            return;
        }

        // Новые массивы и GPU-текстуры пусты до первой публикации нового
        // размера: прежняя картинка к ним уже не относится.
        WithdrawPublication();
        _heldCompletion = null;
        Width = size.x;
        Height = size.y;
        IsInitialized = true;
        Origin = new Vector2Int(int.MinValue, int.MinValue);
        _driver.EnsureCapacity(Width, Height);
        _cellIDMesh.EnsureSize(Width, Height, _cellSize);
        FrameEventLog.Record($"террейн: окно пересоздано {Width}×{Height}");
        _dirty.Clear();
        NeedsRefresh = true;
    }

    /// <summary>
    /// Заплатки перестали окупаться — тексели окна собираются целиком, но кэш
    /// клеток по-прежнему перечитывается только в изменённых местах.
    /// </summary>
    public void CoalesceDirtyRects()
    {
        if (!_rebuildAllCells && !_dirty.IsEmpty &&
            _dirty.PrefersFullRebuild(BuildOrigin, Width, Height))
        {
            _rebuildAllCells = true;
        }
    }

    /// <summary>
    /// Учесть изменение мира. Возвращает true, если изменение задевает окно
    /// сборки (с каймой соседства) и значит изменит опубликованную картинку.
    /// </summary>
    ///
    /// Изменение вне окна не копится и не считается изменением содержимого:
    /// оно приедет вместе с окном из хранилища. Поднимать ради него ревизию
    /// нельзя — освещение приняло бы ближайшую публикацию за смену
    /// геометрии и пересчитало статику целиком, хотя на экране ничего нет.
    public bool RecordWorldChange(
        int serverX,
        int serverY,
        int width,
        int height,
        int worldHeight)
    {
        if (!HasOrigin && !_builds.IsBusy)
        {
            NeedsRefresh = true;
            return true;
        }

        if (_dirty.Add(
            serverX, serverY, width, height,
            BuildOrigin, Width, Height, worldHeight) is not { } region)
        {
            return false;
        }

        _changedRegions.Add(region);
        if (_oldestChangeTimestamp == 0)
        {
            _oldestChangeTimestamp = Stopwatch.GetTimestamp();
        }

        return true;
    }

    /// <summary>
    /// Лежит ли прямоугольник мира (в координатах Unity) не дальше
    /// <paramref name="marginCells"/> от окна сборки. До первого окна —
    /// всегда да: куда встанет окно, ещё неизвестно.
    /// </summary>
    public bool IsNearBuildWindow(RectInt unityRect, int marginCells)
    {
        if (!IsInitialized || (!HasOrigin && !_builds.IsBusy))
        {
            return true;
        }

        Vector2Int origin = BuildOrigin;
        return unityRect.xMax > origin.x - marginCells &&
            unityRect.xMin < origin.x + Width + marginCells &&
            unityRect.yMax > origin.y - marginCells &&
            unityRect.yMin < origin.y + Height + marginCells;
    }

    /// <summary>
    /// Мир сменился целиком: прежнее опубликованное окно показывает чужие
    /// клетки. Идущий шаг отменяется, окно не рисуется до новой публикации.
    /// </summary>
    public void InvalidateWorld()
    {
        _worldGeneration++;
        FrameEventLog.Record("террейн: смена мира");
        _builds.Cancel();
        _heldCompletion = null;
        NeedsRefresh = true;
        _dirty.Clear();
        _changedRegions.Clear();
        _oldestChangeTimestamp = 0;
        WithdrawPublication();
    }

    /// <summary>Переключатель искажения меняет предрасчёт: применяется к следующему шагу.</summary>
    public void RequestDistortion(bool enabled)
    {
        _requestedDistortion = enabled;
        NeedsRefresh = true;
    }

    public void RequestDistortionStyle(TerrainDistortionStyle style)
    {
        _requestedDistortionStyle = style;
        NeedsRefresh = true;
    }

    /// <summary>Отдать прямоугольники, чья новая геометрия опубликована в этом кадре.</summary>
    ///
    /// Прямоугольники уже слиты по правилам DirtyRectSet и обрезаны окном с
    /// каймой. Если после слияния их всё равно слишком много (чанки сыплются
    /// вразнобой), освещение получает один охватывающий прямоугольник: один
    /// пересчёт по маске дешевле сотни мелких очередей.
    public void TakePublishedChangedRegions(List<RectInt> destination)
    {
        const int MaximumSeparateRegions = 16;
        int count = _publishedChangedRegions.Count;
        if (count == 0)
        {
            return;
        }

        if (count <= MaximumSeparateRegions)
        {
            for (int index = 0; index < count; index++)
            {
                destination.Add(_publishedChangedRegions[index]);
            }
        }
        else
        {
            RectInt bounds = _publishedChangedRegions[0];
            for (int index = 1; index < count; index++)
            {
                RectInt rect = _publishedChangedRegions[index];
                int minX = Math.Min(bounds.xMin, rect.xMin);
                int minY = Math.Min(bounds.yMin, rect.yMin);
                int maxX = Math.Max(bounds.xMax, rect.xMax);
                int maxY = Math.Max(bounds.yMax, rect.yMax);
                bounds = new RectInt(minX, minY, maxX - minX, maxY - minY);
            }

            destination.Add(bounds);
        }

        _publishedChangedRegions.Clear();
    }

    /// <summary>
    /// Забрать готовый шаг, если он есть. Зовётся в начале кадра, до
    /// планирования: тогда планировщик видит уже новое начало окна, и
    /// следующий шаг ставится в этом же кадре, а не кадром позже. Возвращает
    /// false при отказе сборки.
    /// </summary>
    ///
    /// Сразу после публикации вызывающий обязан выгрузить тексели (Commit) в
    /// том же кадре: начало окна и двери уже новые.
    public bool TryPublishCompleted(in TerrainBuildServices services, out Exception? failure)
    {
        failure = null;

        // Удержанный шаг публикуется, только пока рабочий поток свободен:
        // идущий шаг уже пишет в те же массивы конвейера. Тогда удержанный
        // заменит этот шаг — он тоже собран целиком.
        if (!HoldPublication && !_builds.IsBusy && _heldCompletion is { } held)
        {
            _heldCompletion = null;
            if (!Complete(services, held, out failure))
            {
                return false;
            }
        }

        if (!_builds.IsBusy ||
            !_builds.TryTakeCompleted(
                out TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult> completion))
        {
            return true;
        }

        services.Telemetry.TerrainBuildInFlight = 0;
        if (HoldPublication && completion.Result != null &&
            completion.Request.WorldGeneration == _worldGeneration)
        {
            // Удерживается только последний готовый шаг: каждый шаг перехода
            // собирает окно целиком, и новый заменяет прежний без остатка.
            _heldCompletion = completion;
            _buildRefreshesTextures = false;
            BuildState = TerrainBuildState.WaitingForPublication;
            return true;
        }

        _heldCompletion = null;
        return Complete(services, completion, out failure);
    }

    /// <summary>
    /// Поставить следующий шаг к запрошенному началу, если окно свободно и
    /// есть что делать. Возвращает false при отказе сборки.
    /// </summary>
    public bool Process(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        Vector2Int requestedOrigin,
        bool dimensionsChanged,
        bool bypassCpuMeshRebuild,
        MeshRenderer? meshRenderer,
        ulong contentRevision,
        out Exception? failure)
    {
        failure = null;
        services.Telemetry.TerrainBuildInFlight = _builds.IsBusy ? 1 : 0;
        if (_builds.IsBusy)
        {
            BuildState = TerrainBuildState.CpuPreparing;
            return true;
        }

        if (bypassCpuMeshRebuild)
        {
            _wasCpuMeshRebuildBypassed = true;
            BuildState = TerrainBuildState.Canceled;
            return true;
        }

        if (_wasCpuMeshRebuildBypassed)
        {
            _wasCpuMeshRebuildBypassed = false;
            NeedsRefresh = true;
        }

        bool rebuild = requestedOrigin != ProspectiveOrigin || NeedsRefresh || dimensionsChanged ||
            !_dirty.IsEmpty || _pendingTextureCellTypes.Count > 0;
        if (!rebuild)
        {
            BuildState = _heldCompletion != null ? TerrainBuildState.WaitingForPublication
                : CellsCommitted ? TerrainBuildState.Published
                : TerrainBuildState.WaitingForData;
            return true;
        }

        return TrySchedule(
            services,
            clientConfigManager,
            requestedOrigin,
            dimensionsChanged,
            meshRenderer,
            contentRevision,
            out failure);
    }

    /// <summary>
    /// Одна выгрузка текселей за кадр, в кадре публикации. Начало окна
    /// публикуется вместе с ними: шейдер берёт по нему кольцевой адрес.
    /// Возвращает время выгрузки в миллисекундах или ноль, если выгружать нечего.
    /// </summary>
    public float Commit()
    {
        if (!_cellTexturesDirty || !HasOrigin || _cellIDMesh.Mesh == null)
        {
            return 0f;
        }

        _cellTexturesDirty = false;
        float uploadMs = _driver.Commit(Origin.x, Origin.y);
        CellsCommitted = true;
        return uploadMs;
    }

    public void Dispose()
    {
        // Рабочий поток пишет только в управляемые массивы конвейера. GPU-
        // ресурсы ниже он не трогает, поэтому их освобождение его не ждёт.
        _builds.Dispose();
        _cellIDMesh.Dispose();
        _driver.Dispose();
    }

    private bool TrySchedule(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        Vector2Int origin,
        bool dimensionsChanged,
        MeshRenderer? meshRenderer,
        ulong contentRevision,
        out Exception? failure)
    {
        failure = null;
        if (!_driver.TryBeginBuild(
            services,
            clientConfigManager,
            out TerrainBuildContext context,
            out bool materialsChanged))
        {
            BuildState = TerrainBuildState.WaitingForData;
            return false;
        }

        if (materialsChanged)
        {
            // Прежние атласы и материалы уже уничтожены: прежние тексели
            // ссылаются на несуществующие индексы. Окно не рисуется, пока новый
            // набор не собран целиком.
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterials = _driver.Materials.CellMaterials;
            }

            if (CellsCommitted)
            {
                WithdrawPublication();
            }
        }

        if (_requestedDistortion is { } distortion)
        {
            _driver.Pipeline.EnableDistortion = distortion;
            _requestedDistortion = null;
        }

        if (_requestedDistortionStyle is { } distortionStyle)
        {
            _driver.Pipeline.DistortionStyle = distortionStyle;
            _requestedDistortionStyle = null;
        }

        // Во время перехода конвейер мог уйти вперёд неопубликованным шагом:
        // приращение к нему не с чем сверить, поэтому шаг перехода — целиком.
        bool forceFull = HoldPublication || _heldCompletion != null ||
            NeedsRefresh || dimensionsChanged || !HasOrigin || materialsChanged ||
            Math.Abs((long)origin.x - Origin.x) >= Width ||
            Math.Abs((long)origin.y - Origin.y) >= Height;
        RebuildLedger.Count(
            dimensionsChanged ? _RebuildResize
            : forceFull ? _RebuildRefresh
            : origin != Origin ? _RebuildGridMove
            : _RebuildPatch);

        // Набор приехавших типов отдаётся шагу целиком и сразу заменяется
        // пустым: текстура, приехавшая во время подготовки, ложится в новый
        // набор и станет следующим шагом, а не меняет перебираемый.
        (_buildTextureCellTypes, _pendingTextureCellTypes) = (_pendingTextureCellTypes, _buildTextureCellTypes);
        _pendingTextureCellTypes.Clear();
        TerrainCpuBuildRequest request;
        try
        {
            request = _driver.Prepare(
                context,
                origin,
                forceFull,
                materialsChanged || _rebuildAllCells,
                _dirty.Rects,
                _buildTextureCellTypes,
                contentRevision,
                _worldGeneration);
        }
        catch (Exception exception)
        {
            BuildState = TerrainBuildState.Error;
            failure = exception;
            return false;
        }

        // Всё, что шаг взял, из очереди снимается сразу: изменения, пришедшие
        // во время шага, копятся заново и станут следующим шагом.
        _buildRefreshesTextures = _buildTextureCellTypes.Count > 0;
        _buildTextureCellTypes.Clear();
        _dirty.Clear();
        _rebuildAllCells = false;

        // Списки меняются местами, а не копируются: шаг за шагом без аллокаций.
        (_buildChangedRegions, _changedRegions) = (_changedRegions, _buildChangedRegions);
        _changedRegions.Clear();
        _buildOldestChangeTimestamp = _oldestChangeTimestamp;
        _oldestChangeTimestamp = 0;
        NeedsRefresh = false;
        _buildStartTimestamp = Stopwatch.GetTimestamp();
        _builds.Start(request);
        BuildState = TerrainBuildState.CpuPreparing;
        services.Telemetry.TerrainBuildInFlight = 1;
        return true;
    }

    private bool Complete(
        in TerrainBuildServices services,
        in TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult> completion,
        out Exception? failure)
    {
        failure = null;
        TerrainCpuBuildRequest request = completion.Request;
        _buildRefreshesTextures = false;
        List<RectInt> changedRegions = _buildChangedRegions;

        if (completion.Result is not { } result)
        {
            // Шаг прерван между стадиями: кэш, предрасчёт, заливка и тексели
            // больше не согласованы, и следующий шаг обязан собрать окно
            // целиком. Изменения шага возвращаются в очередь освещения.
            NeedsRefresh = true;
            _changedRegions.AddRange(changedRegions);
            changedRegions.Clear();
            if (_buildOldestChangeTimestamp != 0 &&
                (_oldestChangeTimestamp == 0 || _buildOldestChangeTimestamp < _oldestChangeTimestamp))
            {
                _oldestChangeTimestamp = _buildOldestChangeTimestamp;
            }

            _buildOldestChangeTimestamp = 0;
            if (completion.WasCanceled)
            {
                services.Telemetry.TerrainBuildCancelCount++;
                BuildState = TerrainBuildState.Canceled;
                return true;
            }

            BuildState = TerrainBuildState.Error;
            failure = completion.Failure;
            return false;
        }

        if (request.WorldGeneration != _worldGeneration ||
            request.Size != new Vector2Int(Width, Height))
        {
            // Шаг досчитан по миру или размеру, которых больше нет.
            NeedsRefresh = true;
            BuildState = TerrainBuildState.Canceled;
            return true;
        }

        if (!_driver.TryContinueBuild(services, out TerrainBuildContext context))
        {
            // Атласы пропали между постановкой и публикацией (сброс набора
            // текстур): тексели ссылаются на них, публиковать нельзя.
            NeedsRefresh = true;
            WithdrawPublication();
            BuildState = TerrainBuildState.WaitingForData;
            return true;
        }

        float latencySeconds = (float)((Stopwatch.GetTimestamp() - _buildStartTimestamp) /
            (double)Stopwatch.Frequency);
        try
        {
            _driver.Publish(context, request, result, latencySeconds * 1000f);
        }
        catch (Exception exception)
        {
            BuildState = TerrainBuildState.Error;
            failure = exception;
            return false;
        }

        Origin = request.Origin;
        if (_transform != null)
        {
            _transform.position = new Vector3(
                request.Origin.x * _cellSize,
                request.Origin.y * _cellSize,
                0f);
        }

        _cellTexturesDirty = true;
        PublishedContentRevision = request.ContentRevision;
        var lightingBounds = new RectInt(Origin.x - 1, Origin.y - 1, Width + 2, Height + 2);
        for (int index = 0; index < changedRegions.Count; index++)
        {
            _publishedChangedRegions.Add(changedRegions[index], lightingBounds);
        }

        changedRegions.Clear();
        BuildState = TerrainBuildState.Published;

        ObservePreparationLatency(latencySeconds);
        services.Telemetry.TerrainWorkerBuildMs = result.ElapsedMs;
        services.Telemetry.TerrainBuildLatencyMs = latencySeconds * 1000f;
        if (_buildOldestChangeTimestamp != 0)
        {
            services.Telemetry.TerrainEditDisplayLatencyMs = (float)(
                (Stopwatch.GetTimestamp() - _buildOldestChangeTimestamp) * 1000.0 / Stopwatch.Frequency);
            _buildOldestChangeTimestamp = 0;
        }

        return true;
    }

    private void WithdrawPublication()
    {
        CellsCommitted = false;
        _cellTexturesDirty = false;
        _driver.HideDoorOverlay();
    }

    private void ObservePreparationLatency(float observedSeconds)
    {
        if (float.IsNaN(observedSeconds) || float.IsInfinity(observedSeconds) || observedSeconds < 0f)
        {
            return;
        }

        // Верхняя граница — чтобы одна холодная сборка при входе в мир не
        // раздувала опережение на всю дальнейшую ходьбу.
        float clamped = Mathf.Clamp(observedSeconds, 0.005f, 2f);
        _preparationLatencySeconds = Mathf.Lerp(_preparationLatencySeconds, clamped, 0.25f);
    }
}
