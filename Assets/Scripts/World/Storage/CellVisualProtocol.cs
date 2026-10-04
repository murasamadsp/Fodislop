#nullable enable

using System;
using MinesServer.Data;

namespace Kern.World;

/// <summary>
/// Единая точка переключения источника визуального протокола.
/// </summary>
public static class CellVisualProtocolRegistry
{
    private static ICellVisualProtocol s_current = new LegacyCellVisualProtocol();

    public static ICellVisualProtocol Current => s_current;

    public static void Replace(ICellVisualProtocol protocol)
    {
        s_current = protocol ?? throw new ArgumentNullException(nameof(protocol));
    }
}

/// <summary>
/// Визуальный протокол, считываемый из BlockRegistry (cells.json).
/// </summary>
public sealed class LegacyCellVisualProtocol : ICellVisualProtocol
{
    private static readonly CellVisualProperties[] s_properties = BuildProperties();

    public LegacyCellVisualProtocol()
    {
    }

    public CellVisualProperties Get(CellType type)
    {
        return s_properties[(byte)type];
    }

    private static CellVisualProperties[] BuildProperties()
    {
        var properties = new CellVisualProperties[256];
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            CellVisualFlags flags = CellVisualFlags.None;
            if (def.CanRoundCorners)
            {
                flags |= CellVisualFlags.CanRoundCorners;
            }

            if (def.IsRoad)
            {
                flags |= CellVisualFlags.Road;
            }

            if (def.IsFluid)
            {
                flags |= CellVisualFlags.Fluid;
            }

            if (def.IsCrystalVein)
            {
                flags |= CellVisualFlags.CrystalVein;
            }

            if (def.IsSolidRockBed)
            {
                flags |= CellVisualFlags.SolidRockBed;
            }

            if (string.Equals(def.ReliefRimFamily, "GreenBlueRock", StringComparison.OrdinalIgnoreCase))
            {
                flags |= CellVisualFlags.GreenBlueRockBed;
            }

            if (string.Equals(def.StructurePartType, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                flags |= CellVisualFlags.BuildingWall;
            }

            if (string.Equals(def.StructurePartType, "Corner", StringComparison.OrdinalIgnoreCase))
            {
                flags |= CellVisualFlags.BuildingCorner;
            }

            if (string.Equals(def.StructurePartType, "Door", StringComparison.OrdinalIgnoreCase))
            {
                flags |= CellVisualFlags.BuildingDoor;
            }

            properties[(byte)type] = new CellVisualProperties(flags);
        }

        return properties;
    }
}
