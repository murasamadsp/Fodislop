#nullable enable

using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using Kern.Core.Interfaces;
#endif
using MinesServer.Data;

namespace Kern.World.Terrain;

public struct CachedCellInfo
{
    public CellType Type;
}

public interface ICachedCellDataProvider
{
    CachedCellInfo GetCell(int x, int y);
}

#if UNITY_5_3_OR_NEWER
public interface ITerrainMetadataLookup
{
    bool TryGet(CellType type, out CellMetadata metadata);
}

public interface ITerrainCellDataSource : ICachedCellDataProvider, ITerrainMetadataLookup
{
    ITerrainMetadataLookup MetadataLookup { get; }

    int CacheMinX { get; }

    int CacheMinY { get; }

    // Окно с каймой в клетку с каждой стороны.
    int CacheWidth { get; }

    int CacheHeight { get; }

    CachedCellData GetCellData(int x, int y);

    void BeginMetadataPass();

    CellMetadata GetMetadata(
        CellType type,
        IMapDataProvider mapData,
        ITextureService textureService,
        IReadOnlyList<IAtlasDescriptor> atlases);
}
#else
// The standalone terrain tests only compile the geometry contracts. The Unity
// build adds metadata resolution and engine service references above.
public interface ITerrainCellDataSource : ICachedCellDataProvider
{
    int CacheMinX { get; }

    int CacheMinY { get; }

    // Окно с каймой в клетку с каждой стороны.
    int CacheWidth { get; }

    int CacheHeight { get; }

    CachedCellData GetCellData(int x, int y);
}
#endif
