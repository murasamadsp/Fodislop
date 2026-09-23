#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Накладка дверей: сбор её квадов и передача их отдельному рендереру.
/// </summary>
///
/// Дверь рисуется поверх террейна собственным мешем вершин, а не текселем
/// клетки: у неё свой порядок сортировки, и она обязана лечь над соседними
/// блоками. Клеток с дверью мало, поэтому их квады собираются по требованию —
/// только когда сборка их задела.
public sealed class TerrainDoorOverlayBuilder : IDisposable
{
    private readonly TerrainDoorOverlayRenderer _renderer = new();
    private readonly List<TerrainVertex> _vertices = [];
    private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

    public void Rebuild(
        TerrainCellBuilder cellBuilder,
        in TerrainCellSources sources,
        int minX,
        int minY,
        Transform parent,
        ISceneObjectFactory sceneObjects,
        Material[] overlayMaterials,
        string sortingLayerName,
        int sortingOrder,
        int meshWidth,
        int meshHeight,
        float cellSize)
    {
        if (!cellBuilder.HasDoors)
        {
            _renderer.Hide();
            return;
        }

        EnsureSubMeshIndices(sources.Atlases.Count);
        cellBuilder.BuildDoorOverlay(sources, minX, minY, _vertices, _subMeshIndices);
        _renderer.Rebuild(
            parent,
            sceneObjects,
            _vertices,
            _subMeshIndices,
            overlayMaterials,
            sortingLayerName,
            sortingOrder,
            meshWidth,
            meshHeight,
            cellSize);
    }

    /// <summary>
    /// Сдвиг сетки не меняет состав дверей: накладке достаточно переехать
    /// вместе с родителем, пересобирать её квады незачем.
    /// </summary>
    public void CompensateParentTranslation(Vector3 parentDelta) =>
        _renderer.CompensateParentTranslation(parentDelta);

    /// <summary>Опубликованного окна больше нет: его двери показывать нельзя.</summary>
    public void Hide() => _renderer.Hide();

    public void Dispose() => _renderer.Dispose();

    private void EnsureSubMeshIndices(int atlasCount)
    {
        if (_subMeshIndices.Length == atlasCount)
        {
            return;
        }

        _subMeshIndices = new List<int>[atlasCount];
        for (int atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
        {
            _subMeshIndices[atlasIndex] = [];
        }
    }
}
