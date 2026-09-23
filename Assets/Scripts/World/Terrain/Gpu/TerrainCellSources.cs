#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;

namespace Kern.World.Terrain;

// Всё, из чего собирается клетка террейна.
public readonly record struct TerrainCellSources(
    ITerrainCellDataSource CellCache,
    TerrainPrecalculator Precalc,
    BackgroundFloodFill FloodFill,
    int WorldWidth,
    int WorldHeight,
    IReadOnlyList<IAtlasDescriptor> Atlases,
    IMapDataProvider? MapData,
    ITextureService? TextureService)
{
    /// <summary>
    /// Сервисы разрешения типов есть только у главного потока. У фоновой
    /// сборки их нет: она читает метаданные, разрешённые до её старта.
    /// </summary>
    public bool CanResolveMetadata => MapData != null && TextureService != null;

    // Только чтение уже разрешённых типов. Сборка клетки не имеет права
    // разрешать тип сама: см. TerrainMetadataWarmup.
    public ITerrainMetadataLookup MetadataLookup => CellCache.MetadataLookup;
}
