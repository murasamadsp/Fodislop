#nullable enable

using System;
using MinesServer.Data;

namespace Kern.World;

public enum TerrainRimFamily : byte
{
    None = 0,
    Crystal = 1,
    Rock = 2,
    GreenBlueRock = 3,
}

[Flags]
public enum CellVisualFlags : ushort
{
    None = 0,
    CanRoundCorners = 1 << 0,
    Road = 1 << 1,
    CrystalVein = 1 << 2,
    SolidRockBed = 1 << 3,
    Fluid = 1 << 4,
    GreenBlueRockBed = 1 << 5,
    BuildingWall = 1 << 6,
    BuildingCorner = 1 << 7,
    BuildingDoor = 1 << 8,
}

public readonly record struct CellVisualProperties(CellVisualFlags Flags)
{
    public bool CanRoundCorners => (Flags & CellVisualFlags.CanRoundCorners) != 0;
    public bool IsRoad => (Flags & CellVisualFlags.Road) != 0;
    public bool IsCrystalVein => (Flags & CellVisualFlags.CrystalVein) != 0;
    public bool IsSolidRockBed => (Flags & CellVisualFlags.SolidRockBed) != 0;
    public bool IsFluid => (Flags & CellVisualFlags.Fluid) != 0;
    public bool IsGreenBlueRockBed => (Flags & CellVisualFlags.GreenBlueRockBed) != 0;
    public bool IsBuildingWall => (Flags & CellVisualFlags.BuildingWall) != 0;
    public bool IsBuildingCorner => (Flags & CellVisualFlags.BuildingCorner) != 0;
    public bool IsBuildingDoor => (Flags & CellVisualFlags.BuildingDoor) != 0;
    public bool IsBuilding => (Flags & (CellVisualFlags.BuildingWall | CellVisualFlags.BuildingCorner | CellVisualFlags.BuildingDoor)) != 0;
    public bool IsContinuousBed => IsCrystalVein || IsSolidRockBed || IsGreenBlueRockBed;

    public TerrainRimFamily RimFamily =>
        IsGreenBlueRockBed ? TerrainRimFamily.GreenBlueRock :
        IsSolidRockBed ? TerrainRimFamily.Rock :
        IsCrystalVein ? TerrainRimFamily.Crystal :
        TerrainRimFamily.None;
}

public interface ICellVisualProtocol
{
    CellVisualProperties Get(CellType type);
}
