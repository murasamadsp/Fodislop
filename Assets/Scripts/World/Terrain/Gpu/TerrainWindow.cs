#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Окно террейна: где оно стоит, что в нём просрочено и как оно доводится до
/// текселей на GPU.
/// </summary>
///
/// У окна три состояния, и все три меняются здесь: начало (куда переехала
/// сетка), просроченные клетки (что изменил мир) и признак «тексели выгружены».
/// Вместе они решают единственный вопрос кадра — собрать окно целиком,
/// заплатать изменённое или не делать ничего.
///
/// Порядок между ними не свободный: заплатка перед сдвигом применяется в СТАРЫХ
/// координатах, потому что сдвиг переносит перекрытие дословно.
public sealed class TerrainWindow : IDisposable
{
    private static readonly RebuildLedger.Entry _RebuildResize = RebuildLedger.Register("Террейн · полная: смена размера сетки");
    private static readonly RebuildLedger.Entry _RebuildGridMove = RebuildLedger.Register("Террейн · полная: сдвиг сетки");
    private static readonly RebuildLedger.Entry _RebuildRefresh = RebuildLedger.Register("Террейн · полная: флаг обновления");
    private static readonly RebuildLedger.Entry _RebuildPatch = RebuildLedger.Register("Террейн · частичная: изменённые клетки");

    private readonly TerrainBuildDriver _driver = new();
    private readonly TerrainDirtyTracker _dirty = new();
    private readonly HashSet<CellType> _pendingTextureCellTypes = [];

    // Меш идентификаторов всей сетки: поле материалов рисуется целиком, без
    // смещения показа, поэтому у него собственный меш на весь прямоугольник.
    private readonly TerrainCellIDMesh _cellIDMesh = new();

    private Transform? _transform;
    private float _cellSize = 1f;
    private bool _cellTexturesDirty = true;
    private bool _wasCpuMeshRebuildBypassed;

    public TerrainBuildDriver Driver => _driver;

    public TerrainDirtyTracker Dirty => _dirty;

    public HashSet<CellType> PendingTextureCellTypes => _pendingTextureCellTypes;

    public Mesh? CellIDMesh => _cellIDMesh.Mesh;

    public Vector2Int Origin { get; private set; } = new(int.MinValue, int.MinValue);

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool CellsCommitted { get; private set; }

    /// <summary>Перекрытие переносить нельзя: содержимое окна изменилось целиком.</summary>
    public bool NeedsRefresh { get; set; }

    public bool HasOrigin => Origin.x != int.MinValue;

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
    public void ApplyDimensions(Vector2Int size, bool dimensionsChanged)
    {
        if (!dimensionsChanged && IsInitialized)
        {
            return;
        }

        Width = size.x;
        Height = size.y;
        IsInitialized = true;
        Origin = new Vector2Int(int.MinValue, int.MinValue);
        _driver.EnsureCapacity(Width, Height);
        _cellIDMesh.EnsureSize(Width, Height, _cellSize);
        NeedsRefresh = true;
    }

    /// <summary>Заплатки перестали окупаться — дешевле собрать окно целиком.</summary>
    public void CoalesceDirtyRects()
    {
        if (_dirty.CoalesceIntoFullRebuild(Origin, Width, Height))
        {
            NeedsRefresh = true;
        }
    }

    /// <summary>
    /// Перечитать клетки типов, у которых приехала текстура. Возвращает false,
    /// если перечитывать нечем: атласов ещё нет.
    /// </summary>
    public bool RefreshPendingTextureCells(in TerrainBuildServices services)
    {
        if (!_driver.TryContinueBuild(services, out TerrainBuildContext context))
        {
            return false;
        }

        _driver.RefreshTextureCells(context, _pendingTextureCellTypes, Origin.x, Origin.y);
        _pendingTextureCellTypes.Clear();
        _cellTexturesDirty = true;
        return true;
    }

    /// <summary>
    /// Довести окно до запрошенного начала: собрать, заплатать или ничего.
    /// Возвращает false, если кадр надо бросить.
    /// </summary>
    public bool Process(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        Vector2Int requestedOrigin,
        bool dimensionsChanged,
        bool bypassCpuMeshRebuild,
        MeshRenderer? meshRenderer,
        out Exception? failure)
    {
        failure = null;
        if (bypassCpuMeshRebuild)
        {
            _wasCpuMeshRebuildBypassed = true;
            return true;
        }

        if (_wasCpuMeshRebuildBypassed)
        {
            _wasCpuMeshRebuildBypassed = false;
            NeedsRefresh = true;
        }

        bool rebuild = requestedOrigin != Origin || NeedsRefresh || dimensionsChanged;
        if (!rebuild)
        {
            if (_dirty.IsEmpty)
            {
                return true;
            }

            RebuildLedger.Count(_RebuildPatch);
            if (!TryPatch(services, requestedOrigin))
            {
                return false;
            }

            _cellTexturesDirty = true;
            _dirty.Clear();
            return true;
        }

        // Scroll preserves the overlap verbatim. Apply its pending edits in the
        // OLD coordinate system before moving the rings.
        if (!NeedsRefresh && !dimensionsChanged && !_dirty.IsEmpty &&
            !TryPatch(services, Origin))
        {
            return false;
        }

        RebuildLedger.Count(
            dimensionsChanged ? _RebuildResize
            : requestedOrigin != Origin ? _RebuildGridMove
            : _RebuildRefresh);
        if (!TryBuild(services, clientConfigManager, requestedOrigin, meshRenderer, out failure))
        {
            return false;
        }

        if (_transform != null)
        {
            _transform.position = new Vector3(
                requestedOrigin.x * _cellSize, requestedOrigin.y * _cellSize, 0f);
        }

        Origin = requestedOrigin;
        _cellTexturesDirty = true;
        _dirty.Clear();

        // Перемещение кольцевого terrain-кэша не меняет мировую геометрию.
        // Освещение привязано к стабильному world-region, поэтому scroll не
        // должен поднимать geometry revision и запускать полный static solve.
        return true;
    }

    /// <summary>
    /// Одна выгрузка текселей за кадр, после сборки или заплатки. Начало окна
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
        _cellIDMesh.Dispose();
        _driver.Dispose();
    }

    private bool TryBuild(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        Vector2Int origin,
        MeshRenderer? meshRenderer,
        out Exception? failure)
    {
        failure = null;
        if (!_driver.TryBeginBuild(
            services, clientConfigManager,
            out TerrainBuildContext context, out bool materialsChanged))
        {
            return false;
        }

        try
        {
            _driver.BuildWindow(context, origin.x, origin.y, NeedsRefresh, materialsChanged);
            NeedsRefresh = false;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }

        if (materialsChanged && meshRenderer != null)
        {
            meshRenderer.sharedMaterials = _driver.Materials.CellMaterials;
        }

        return true;
    }

    private bool TryPatch(in TerrainBuildServices services, Vector2Int origin)
    {
        if (_cellIDMesh.Mesh == null ||
            !_driver.TryContinueBuild(services, out TerrainBuildContext context))
        {
            return false;
        }

        _driver.PatchRegions(context, origin.x, origin.y, _dirty.Rects);
        return true;
    }
}
