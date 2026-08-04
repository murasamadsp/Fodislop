#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain
{
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
        public CellConfigProperties Properties;
        public byte ReliefGroup;
        public CellDistortionType Distortion;
        public bool HasTileGroup;
        public int TileGroupId;
        public Color MinimapColor;
        public CellAnimationType Animation;
        public float AnimationSpeed;
        public Vector4 AtlasRect;
        public int AtlasIndex;
        public float UVTileSize;
        public int AnimationFrameCount;
        public float FrameHeightTiles;
    }

    public struct CellMetadata
    {
        public CellConfigProperties Properties;
        public byte ReliefGroup;
        public CellDistortionType Distortion;
        public bool HasTileGroup;
        public int TileGroupId;
        public Color MinimapColor;
        public CellAnimationType Animation;
        public float AnimationSpeed;
        public Vector4 AtlasRect;
        public int AtlasIndex;
        public float UVTileSize;
        public int AnimationFrameCount;
        public float FrameHeightTiles;
        public bool IsTextureReady;
    }
}
