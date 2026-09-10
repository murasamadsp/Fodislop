#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain;
public enum TerrainCellState
{
    Loaded,
    Unloaded,
    OutsideWorld,
}

public struct CachedCellData
{
    /// <summary>
    /// Клетка светится и её свечение анимировано.
    /// </summary>
    /// <remarks>
    /// Предикат живёт здесь, а не у спрашивающих: <c>CellConfigProperties</c> и
    /// <c>CellAnimationType</c> объявлены в сетевых пакетах, и каждый, кому нужен
    /// этот вопрос, иначе тянул бы к себе пространство имён протокола. Рендереру
    /// террейна знать про пакеты незачем.
    /// </remarks>
    public readonly bool HasAnimatedGlow =>
        State == TerrainCellState.Loaded &&
        Animation != CellAnimationType.None &&
        (Properties & CellConfigProperties.Glowing) != 0;

    public TerrainCellState State;
    public CellType Type;
    public CellConfigProperties Properties;
    public byte ReliefGroup;
    public CellDistortionType Distortion;
    public bool HasTileGroup;
    public int TileGroupId;
    public Color32 MinimapColor; // was Color (16 bytes) — Color32 (4 bytes) sufficient for minimap
    public CellAnimationType Animation;
    public float AnimationSpeed;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public float UVTileSize;
    public int AnimationFrameCount;
    public float FrameHeightTiles;
    public bool IsTextureReady;
}

public struct CellMetadata
{
    public CellConfigProperties Properties;
    public byte ReliefGroup;
    public CellDistortionType Distortion;
    public bool HasTileGroup;
    public int TileGroupId;
    public Color32 MinimapColor; // was Color (16 bytes) — Color32 (4 bytes) sufficient for minimap
    public CellAnimationType Animation;
    public float AnimationSpeed;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public float UVTileSize;
    public int AnimationFrameCount;
    public float FrameHeightTiles;
    public bool IsTextureReady;
    /// <summary>True once fully populated; replaces the parallel _metadataReady bool[] in TerrainCellCache.</summary>
    public bool IsPopulated;
}
