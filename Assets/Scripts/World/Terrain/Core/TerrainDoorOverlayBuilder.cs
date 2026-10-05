#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Накладка дверей: состав её квадов и передача их отдельному рендереру.
/// </summary>
///
/// Дверь рисуется поверх террейна собственным мешем, а не только квадом
/// клетки: у неё свой порядок сортировки, и она обязана лечь над соседними
/// блоками. Меш несёт одни адреса дверей; вид шейдер берёт из буфера клеток.
public sealed class TerrainDoorOverlayBuilder : IDisposable
{
    private readonly TerrainDoorOverlayRenderer _renderer = new();
    private readonly List<int> _doorQuads = [];

    public void Rebuild(
        TerrainCellBuilder cellBuilder,
        Transform parent,
        ISceneObjectFactory sceneObjects,
        Material[] cellMaterials,
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

        cellBuilder.CopyDoorQuads(_doorQuads);
        _renderer.Rebuild(
            parent,
            sceneObjects,
            _doorQuads,
            cellMaterials,
            sortingLayerName,
            sortingOrder,
            meshWidth,
            meshHeight,
            cellSize);
    }

    /// <summary>Опубликованного окна больше нет: его двери показывать нельзя.</summary>
    public void Hide() => _renderer.Hide();

    public void Dispose() => _renderer.Dispose();
}
