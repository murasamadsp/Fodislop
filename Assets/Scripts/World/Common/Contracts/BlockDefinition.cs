#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World;

public readonly record struct BlockDefinition
{
    // 1. Физика и базовые свойства
    public bool Passable { get; init; } = false;

    public bool Breakable { get; init; } = false;

    public bool Diggable { get; init; } = false;

    public ushort MoveCooldownMs { get; init; } = 0;

    // 2. Светотень и освещение
    public bool CastsShadow { get; init; } = false;

    public bool ReceivesShadow { get; init; } = false;

    public bool BlendWithNeighbors { get; init; } = false;

    public bool EmitsLight { get; init; } = false;

    // 3. Текстура и шейдерные эффекты
    public int ConnectedTileGroupId { get; init; } = -1;

    public CellDistortionType MeshDistortion { get; init; } = CellDistortionType.Neutral;

    public CellAnimationType ShaderEffect { get; init; } = CellAnimationType.None;

    public byte ShaderEffectSpeed { get; init; } = 0;

    public byte ShaderEffectPhaseOffset { get; init; } = 0;

    public string SurfaceShaderProfile { get; init; } = "Default";

    public string DecalFamily { get; init; } = "None";

    public int PrismaticPaletteIndex { get; init; } = 0;

    // 4. Геометрия террейна и швы
    public byte TerrainSeamGroupId { get; init; } = 0;

    public bool CanRoundCorners { get; init; } = false;

    public bool IsRoad { get; init; } = false;

    public bool IsCrystalVein { get; init; } = false;

    public bool IsSolidRockBed { get; init; } = false;

    public bool IsFluid { get; init; } = false;

    public string ReliefRimFamily { get; init; } = "None";

    // 5. Постройки и интерактивные зоны
    public string StructurePartType { get; init; } = "None";

    public bool IsPackBlock { get; init; } = false;

    public bool IsBuildingBlock { get; init; } = false;

    // 6. Экономика и сбор
    public int CrystalBasketIndex { get; init; } = -1;

    // 7. Карта
    public string? MapColorHex { get; init; } = null;

    public BlockDefinition()
    {
    }
}
