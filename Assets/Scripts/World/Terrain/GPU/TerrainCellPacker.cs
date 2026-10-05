#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

// Упаковка клетки и строки типа для буфера клеток (TerrainCellData). Вид
// типа — единственное место, где из конфига типа получается его строка; вид
// клетки шейдер выводит из клетки и соседей.
//
// Прежняя CPU-сборка вершин квада (FillQuad) живёт в тестах как эталон вида:
// Assets/Scripts/Tests/Editor/World/Terrain/Reference/TerrainQuadBuilder.cs.
internal static class TerrainCellPacker
{
    internal readonly record struct CellRenderProperties(
        Vector4 AtlasRect,
        float UVTileSize,
        CellAnimationType Animation,
        float AnimationSpeed,
        int AnimationFrameCount,
        float FrameHeightTiles,
        bool HasTileGroup,
        int TileGroupId,
        CellConfigProperties Properties,
        Color32 MinimapColor,
        int AtlasIndex,
        CellDistortionType Distortion,
        byte ReliefGroup);

    // Клетка для буфера: тип переднего плана (0 у незагруженной) и тип фона
    // до решения слоя. Остальное, включая узлы сетки, шейдер выводит из
    // соседей. Фон записан и у незагруженной клетки: автотайл фона соседей
    // сверяется с ним так же, как прежняя CPU-сборка.
    internal static TerrainCell PackCell(in TerrainCellSources sources, int x, int y)
    {
        CachedCellInfo cell = sources.CellCache.GetCell(x + 1, y + 1);
        return TerrainCellData.PackCell(
            cell.Type, TerrainCellLayers.ResolveBackground(cell.Type, cell.Properties));
    }

    // Клетка каймы кольца (локально -1 или размер окна): тип переднего плана.
    internal static TerrainCell PackMargin(in TerrainCellSources sources, int x, int y) =>
        TerrainCellData.PackMargin(sources.CellCache.GetCell(x + 1, y + 1).Type);

    internal static CellRenderProperties FromMetadata(in CellMetadata meta) =>
        new(
            meta.AtlasRect,
            meta.UVTileSize,
            meta.Animation,
            meta.AnimationSpeed,
            meta.AnimationFrameCount,
            meta.FrameHeightTiles,
            meta.HasTileGroup,
            meta.TileGroupId,
            meta.Properties,
            meta.MinimapColor,
            meta.AtlasIndex,
            meta.Distortion,
            meta.ReliefGroup);

    /// <summary>Строка таблицы типов: всё, что у типа одинаково во всех клетках.</summary>
    internal static TerrainTypeSurface ResolveTypeSurface(
        CellType cellType,
        in CellMetadata meta,
        IReadOnlyList<IAtlasDescriptor> atlases) =>
        ResolveTypeSurface(cellType, FromMetadata(in meta), atlases);

    // Единственное место, где из конфига типа получается его вид. Им же
    // собираются вершины накладки и строка таблицы типов на GPU.
    internal static TerrainTypeSurface ResolveTypeSurface(
        CellType cellType,
        CellRenderProperties props,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        Vector4 atlasRect = props.AtlasRect;
        float uvTileSize = props.UVTileSize;
        CellAnimationType animType = props.Animation;
        float animSpeed = props.AnimationSpeed;
        int animFrames = props.AnimationFrameCount;
        float frameHeight = props.FrameHeightTiles;
        int atlasIndex = props.AtlasIndex;

        if (atlasIndex < 0 || atlasIndex >= atlases.Count)
        {
            atlasIndex = 0;
        }

        bool hasTexture = atlasRect.z > 0f && atlasRect.w > 0f && uvTileSize > 0f;

        if (!hasTexture)
        {
            atlasRect = Vector4.zero;
            uvTileSize = atlases.Count > 0 ? (1f / atlases[0].Size) : 0f;
            animType = CellAnimationType.None;
            animSpeed = 0f;
            animFrames = 1;
            frameHeight = 1f;
        }

        CellVisualProperties visuals = MapCellConfigCatalog.GetVisualProperties(cellType);
        bool isGlowing = (props.Properties & CellConfigProperties.Glowing) != 0;
        // Непрозрачность — свойство пары «тип, атлас»: шейдер атласов не
        // знает, поэтому оба ответа считаются здесь.
        bool opaqueInAnyAtlas = false;
        for (int index = 0; index < atlases.Count; index++)
        {
            opaqueInAnyAtlas |= atlases[index].IsFullyOpaque(cellType);
        }

        return new TerrainTypeSurface(
            atlasIndex,
            atlasRect,
            uvTileSize,
            animFrames,
            frameHeight,
            animType,
            TerrainAnimationProfileCatalog.Get(cellType, animSpeed),
            props.HasTileGroup,
            props.TileGroupId,
            // Рваный край (Organic) кладёт текстуру полотном, иначе на сдвинутых
            // стыках клеток видны швы; остальное — плиткой.
            visuals.Shape == CellShape.Organic,
            props.ReliefGroup,
            props.MinimapColor,
            isGlowing,
            isGlowing ? Mathf.Max(1f / byte.MaxValue, props.MinimapColor.a / 255f) : 0f,
            // Твёрдое — то, через что нельзя пройти: отбрасывает тень и
            // задерживает свет.
            (props.Properties & CellConfigProperties.Passable) == 0,
            visuals.IsRound,
            TerrainDecalCatalog.GetFamily(cellType),
            visuals.IsBuildingWall,
            visuals.IsBuildingCorner,
            atlasIndex < atlases.Count && atlases[atlasIndex].IsFullyOpaque(cellType),
            opaqueInAnyAtlas,
            props.Distortion,
            cellType == CellType.Empty);
    }
}
