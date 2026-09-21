#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Сервисы, из которых террейн берёт данные для сборки.</summary>
public readonly record struct TerrainBuildServices(
    IWorldDataStorage Storage,
    IMapDataProvider MapData,
    ITextureService TextureService,
    IFrameTelemetry Telemetry);

/// <summary>
/// Сборка террейна целиком: материалы, тексели и накладка дверей.
/// </summary>
///
/// Эти три вещи неразделимы по смыслу. Тексель несёт индекс атласа, поэтому
/// смена набора атласов запрещает переносить перекрытие. Накладка дверей
/// собирается из тех же источников, что тексели, и пересобирается ровно тогда,
/// когда сборка задела дверь. Держать их врозь значит каждый раз вручную
/// вспоминать эти связи — чем рендерер и занимался.
public sealed class TerrainBuildDriver : IDisposable
{
    private readonly TerrainBuildPipeline _pipeline = new();
    private readonly TerrainMaterialManager _materials = new();
    private readonly TerrainDoorOverlayBuilder _doorOverlay = new();

    private Transform? _parent;
    private ISceneObjectFactory? _sceneObjects;
    private string _sortingLayerName = "Default";
    private int _doorOverlaySortingOrder;
    private float _cellSize = 1f;
    private int _meshWidth;
    private int _meshHeight;

    public TerrainBuildPipeline Pipeline => _pipeline;

    public TerrainMaterialManager Materials => _materials;

    /// <summary>Привязка к сцене: у накладки дверей собственный объект под террейном.</summary>
    public void Attach(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize)
    {
        _parent = parent;
        _sceneObjects = sceneObjects;
        _sortingLayerName = sortingLayerName;
        _doorOverlaySortingOrder = doorOverlaySortingOrder;
        _cellSize = cellSize;
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        _meshWidth = meshWidth;
        _meshHeight = meshHeight;
        _pipeline.EnsureCapacity(meshWidth, meshHeight, _cellSize);
    }

    /// <summary>
    /// Подготовить материалы кадра и собрать контекст сборки. Возвращает false,
    /// если строить ещё не из чего: атласы не приехали.
    /// </summary>
    public bool TryBeginBuild(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        out TerrainBuildContext context,
        out bool materialsChanged)
    {
        context = default;
        materialsChanged = false;

        IReadOnlyList<IAtlasDescriptor> atlases = services.TextureService.GetAllAtlases();
        if (atlases == null || atlases.Count == 0)
        {
            return false;
        }

        materialsChanged = _materials.EnsureMaterials(
            atlases, _meshWidth, _meshHeight, clientConfigManager, _pipeline.CellCache);
        FlushAtlases(services, atlases, out context);
        return true;
    }

    /// <summary>Контекст без пересоздания материалов: заплатка их не меняет.</summary>
    public bool TryContinueBuild(
        in TerrainBuildServices services,
        out TerrainBuildContext context)
    {
        context = default;
        IReadOnlyList<IAtlasDescriptor> atlases = services.TextureService.GetAllAtlases();
        if (atlases == null || atlases.Count == 0 || _materials.Materials.Length == 0)
        {
            return false;
        }

        FlushAtlases(services, atlases, out context);
        return true;
    }

    public void BuildWindow(
        in TerrainBuildContext context,
        int minX,
        int minY,
        bool forceFull,
        bool materialsChanged)
    {
        _pipeline.BuildWindow(context, minX, minY, forceFull, materialsChanged);
        _materials.BindAtlasTextures(context.Atlases, context.TextureService);

        if (_pipeline.CellBuilder.DoorsTouched)
        {
            RebuildDoorOverlay(context, minX, minY);
            return;
        }

        if (_pipeline.LastBuildScrolled)
        {
            // Состав дверей не изменился: накладке достаточно переехать вместе
            // с родителем, её квады пересобирать незачем.
            Vector2Int delta = _pipeline.LastScrollDelta;
            _doorOverlay.CompensateParentTranslation(
                new Vector3(delta.x * _cellSize, delta.y * _cellSize, 0f));
        }
    }

    public void PatchRegions(in TerrainBuildContext context, int minX, int minY, DirtyRectSet rects)
    {
        // Накладка пересобирается, только если заплатка задела двери: заплатка
        // на ходу есть почти в каждом кадре, а двери в ней редки.
        if (_pipeline.PatchRegions(context, minX, minY, rects))
        {
            RebuildDoorOverlay(context, minX, minY);
        }
    }

    public void RefreshTextureCells(
        in TerrainBuildContext context,
        HashSet<CellType> cellTypes,
        int minX,
        int minY,
        bool rebuildCells)
    {
        _pipeline.RefreshTextureCells(context, cellTypes, minX, minY, rebuildCells);
        _materials.BindAtlasTextures(context.Atlases, context.TextureService);
        if (_pipeline.CellBuilder.DoorsTouched)
        {
            RebuildDoorOverlay(context, minX, minY);
        }
    }

    public float Commit(int originX, int originY) => _pipeline.Commit(originX, originY);

    public void Dispose()
    {
        _pipeline.Dispose();
        _doorOverlay.Dispose();
        _materials.CleanupMaterials();
    }

    private void FlushAtlases(
        in TerrainBuildServices services,
        IReadOnlyList<IAtlasDescriptor> atlases,
        out TerrainBuildContext context)
    {
        long atlasUploadStart = Stopwatch.GetTimestamp();
        services.TextureService.FlushDirtyAtlases();
        services.Telemetry.TerrainAtlasUploadTimeMs += (float)(
            (Stopwatch.GetTimestamp() - atlasUploadStart) * 1000.0 / Stopwatch.Frequency);

        context = new TerrainBuildContext(
            services.Storage,
            services.MapData,
            services.TextureService,
            atlases,
            services.Telemetry,
            _meshWidth,
            _meshHeight);
    }

    private void RebuildDoorOverlay(in TerrainBuildContext context, int minX, int minY)
    {
        if (_parent == null || _sceneObjects == null)
        {
            throw new InvalidOperationException(
                "Terrain door overlay cannot be built before the driver is attached to the scene.");
        }

        _doorOverlay.Rebuild(
            _pipeline.CellBuilder,
            _pipeline.CreateSources(context),
            minX,
            minY,
            _parent,
            _sceneObjects,
            _materials.OverlayMaterials,
            _sortingLayerName,
            _doorOverlaySortingOrder,
            _meshWidth,
            _meshHeight,
            _cellSize);
    }
}
