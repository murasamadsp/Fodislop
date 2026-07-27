using UnityEngine;
using Fodinae.Scripts.Data;
using MinesServer.Data;

namespace Fodinae.Scripts.World.Terrain
{
    public struct CachedCellData
    {
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
