#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Читающая часть кэша метаданных: ровно то, что нужно сборке клетки.
/// Реализация не аллоцирует, не трогает Unity-API и не меняет состояние,
/// поэтому её можно звать из рабочих потоков <c>Parallel.For</c>.
/// </summary>
public interface ITerrainMetadataLookup
{
    bool TryGet(CellType type, out CellMetadata metadata);
}

/// <summary>
/// Resolves, creates, and caches immutable CellMetadata and CachedCellData templates for cell types.
/// </summary>
///
/// Разрешение типа (<see cref="GetMetadata"/>) обязано идти с главного потока:
/// оно читает конфиг мира, пишет в общий массив и дозаказывает недостающую
/// текстуру через <see cref="ITextureService.RequestTexture"/>. Сборка клетки
/// пользуется только <see cref="TryGet"/>.
public sealed class TerrainCellMetadataCache : ITerrainMetadataLookup
{
    private readonly CellMetadata[] _metadataLookup = new CellMetadata[65536];

    public void Clear()
    {
        Array.Clear(_metadataLookup, 0, _metadataLookup.Length);
    }

    public void Invalidate(HashSet<CellType> cellTypes)
    {
        foreach (CellType cellType in cellTypes)
        {
            int index = (int)cellType;
            if ((uint)index < (uint)_metadataLookup.Length)
            {
                _metadataLookup[index].IsPopulated = false;
            }
        }
    }

    // Чистое чтение уже разрешённого типа. Промах — не повод что-то
    // досчитывать: значит, прогрев не покрыл тип, и это дефект вызывающего.
    public bool TryGet(CellType type, out CellMetadata metadata)
    {
        int idx = (int)type;
        if ((uint)idx < (uint)_metadataLookup.Length && _metadataLookup[idx].IsPopulated)
        {
            metadata = _metadataLookup[idx];
            return true;
        }

        metadata = default;
        return false;
    }

    public CellMetadata GetMetadata(
        CellType type,
        IMapDataProvider mm,
        ITextureService wtm,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        // Готовый тип заморожен: его rect и анимация больше не меняются.
        // Неготовый перерешается, пока текстура не приедет, — иначе запись
        // в кэш навсегда закрепила бы состояние «текстуры нет».
        int idx = (int)type;
        if ((uint)idx < (uint)_metadataLookup.Length &&
            _metadataLookup[idx].IsPopulated &&
            _metadataLookup[idx].IsTextureReady)
        {
            return _metadataLookup[idx];
        }

        var config = mm.GetCellConfig(type);

        int atlasIndex = -1;
        for (int i = 0; i < atlases.Count; i++)
        {
            if (atlases[i].ContainsCell(type))
            {
                atlasIndex = i;
                break;
            }
        }

        Vector4 atlasRect = wtm.GetCellFrameRect(type);
        int frameCount = wtm.GetAnimationFrameCount(type);
        int frameSize = wtm.GetFrameSize(type);

        CellConfigProperties properties = config.Properties;
        if (MapCellConfigCatalog.IsBuildingOrArtificialBlock(type))
        {
            properties &= ~CellConfigProperties.Glowing;
        }

        var meta = new CellMetadata
        {
            Properties = properties,
            ReliefGroup = config.ReliefGroup,
            Distortion = config.Distortion,
            HasTileGroup = mm.TryGetTileGroup(type, out int gid),
            TileGroupID = gid,
            MinimapColor = (Color32)mm.GetCellMinimapColor(type),
            Animation = config.Animation,
            AnimationSpeed = wtm.GetAnimationSpeedForCell(type),
            AtlasRect = atlasRect,
            AtlasIndex = atlasIndex,
            UVTileSize = atlasIndex >= 0 && atlasIndex < atlases.Count
                ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size
                : 0f,
            AnimationFrameCount = frameCount,
            FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
            IsTextureReady = atlasIndex >= 0 && atlasRect.z > 0f,
            IsPopulated = true,
        };

        // Кладётся и неготовый тип: сборка клетки читает только этот массив
        // и не имеет права разрешать тип сама.
        if ((uint)idx < (uint)_metadataLookup.Length)
        {
            _metadataLookup[idx] = meta;
        }

        if (!meta.IsTextureReady)
        {
            wtm.RequestTexture(type);
        }

        return meta;
    }

    public CachedCellData CreateCachedData(CellType type, CellMetadata meta)
    {
        return new CachedCellData
        {
            State = TerrainCellState.Loaded,
            Type = type,
            Properties = meta.Properties,
            ReliefGroup = meta.ReliefGroup,
            Distortion = meta.Distortion,
            HasTileGroup = meta.HasTileGroup,
            TileGroupID = meta.TileGroupID,
            MinimapColor = meta.MinimapColor,
            Animation = meta.Animation,
            AnimationSpeed = meta.AnimationSpeed,
            AtlasRect = meta.AtlasRect,
            AtlasIndex = meta.AtlasIndex,
            UVTileSize = meta.UVTileSize,
            AnimationFrameCount = meta.AnimationFrameCount,
            FrameHeightTiles = meta.FrameHeightTiles,
            IsTextureReady = meta.IsTextureReady,
        };
    }
}
