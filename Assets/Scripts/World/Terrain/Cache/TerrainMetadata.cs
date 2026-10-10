#nullable enable

using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;
public enum TerrainCellState
{
    Loaded,
    Unloaded,
    OutsideWorld,
}

public struct CachedCellData
{
    public TerrainCellState State;
    public CellType Type;
    public byte RimMass;
    public CellOutline Outline;
    public bool HasTileGroup;
    public int TileGroupId;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public int AnimationFrameCount;
    public bool IsTextureReady;
}

public struct CellMetadata
{
    public byte RimMass;
    public CellOutline Outline;
    public bool HasTileGroup;
    public int TileGroupId;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public int AnimationFrameCount;
    public bool IsTextureReady;
    public bool IsPopulated;
}
