#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
    public static BlockDefinition GetBlockDefinition(CellType type) =>
        BlockRegistry.Get(type);

    // Правило экономики сервера, а не свойство блока: добыча кристалла кладёт
    // его в корзину, слот — номер CrystalType в BasketPacket.
    public static CrystalType GetMinedCrystal(CellType cell) => cell switch
    {
        CellType.Green => CrystalType.Green,
        CellType.Blue => CrystalType.Blue,
        CellType.Red => CrystalType.Red,
        CellType.Violet => CrystalType.Violet,
        CellType.White => CrystalType.White,
        CellType.Cyan => CrystalType.Cyan,
        _ => CrystalType.Unknown,
    };

    // Правило сервера: части пака (стена, угол, дверь) стыкуются друг с
    // другом как одна группа автотайла.
    public static byte[][] CreateTileGroups()
    {
        var pack = new List<byte>();
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            if (def.StructurePart is CellStructurePart.Wall or CellStructurePart.Corner or CellStructurePart.Door)
            {
                pack.Add((byte)type);
            }
        }

        pack.Sort();
        return [pack.ToArray()];
    }

    public static CellConfigurationPacket[] CreateCellConfigurations()
    {
        var configs = new CellConfigurationPacket[256];
        for (int i = 0; i < 256; i++)
        {
            var type = (CellType)i;
            BlockDefinition def = BlockRegistry.Get(type);

            CellConfigProperties props = CellConfigProperties.None;
            if (def.Passable)
            {
                props |= CellConfigProperties.Passable;
            }

            // Правило сервера: ломается всё непроходимое, кроме построек.
            if (!def.Passable && def.StructurePart == CellStructurePart.None)
            {
                props |= CellConfigProperties.Breakable;
            }

            if (def.EmitsLight)
            {
                props |= CellConfigProperties.Glowing;
            }

            configs[i] = new CellConfigurationPacket
            {
                Properties = props,
                Distortion = def.Shape switch
                {
                    CellShape.Flat => CellDistortionType.Neutral,
                    CellShape.Organic => CellDistortionType.Cause,
                    _ => CellDistortionType.Block,
                },
                // Мигание и мерцание — анимации сервера, со сдвигом фазы в кадр.
                Animation = def.Surface switch
                {
                    CellSurface.Blinking => CellAnimationType.Blinking,
                    CellSurface.Shimmer => CellAnimationType.Shimmer,
                    _ => CellAnimationType.None,
                },
                AnimationSpeed = def.SurfaceSpeed,
                FrameOffset = (byte)(def.Surface is CellSurface.Blinking or CellSurface.Shimmer ? 1 : 0),
                ReliefGroup = def.RimGroup,
                Color = DummyMapColors.Get(i),
            };
        }

        return configs;
    }

    public static Dictionary<CellType, ushort> CreateMovementSpeeds(
        CellConfigurationPacket[] configurations)
    {
        var speeds = new Dictionary<CellType, ushort>(BlockRegistry.Blocks.Count);
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            // Правило сервера: по проходимому — быстро, по остальному — медленно.
            speeds[type] = (ushort)(def.Passable ? 20 : 100);
        }

        return speeds;
    }

    public static ItemType PickRandomBonusItem(Random random)
    {
        var items = new[]
        {
            ItemType.Teleport, ItemType.Compressor, ItemType.C190, ItemType.Trans,
            ItemType.Nano, ItemType.Battery, ItemType.ConstructionBot, ItemType.PortableTeleporter,
            ItemType.Scanner, ItemType.GeoBlackRock, ItemType.GeoRedRock, ItemType.Cred,
            ItemType.GeoCyan, ItemType.GeoHypno, ItemType.Rem, ItemType.Charge,
            ItemType.Geopack, ItemType.Poly, ItemType.RazBomb, ItemType.ProtonBomb,
        };
        return items[random.Next(items.Length)];
    }

    public static long PickRandomAmount(ItemType item, Random random)
    {
        return item switch
        {
            ItemType.Teleport or ItemType.PortableTeleporter => 1,
            ItemType.Cred => random.Next(5, 11),
            ItemType.Rem => random.Next(50, 101),
            ItemType.Geopack => random.Next(10, 16),
            _ => random.Next(5, 20),
        };
    }
}
