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
            properties[(byte)type] = new CellVisualProperties(
                def.Shape,
                def.DecalFamily,
                def.Surface switch
                {
                    CellSurface.Molten => TerrainAnimationProfile.MoltenSurface,
                    CellSurface.Faceted => TerrainAnimationProfile.FacetedCrystal,
                    CellSurface.Prismatic => TerrainAnimationProfile.PrismaticCrystal,
                    _ => TerrainAnimationProfile.Default,
                },
                def.SurfacePalette,
                def.StructurePart);
        }

        return properties;
    }
}
