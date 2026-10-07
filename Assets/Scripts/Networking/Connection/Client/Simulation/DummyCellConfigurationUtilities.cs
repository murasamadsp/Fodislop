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
            if (def.Outline is CellOutline.Wall or CellOutline.Corner or CellOutline.Door)
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
            if (def.IsPassable)
            {
                props |= CellConfigProperties.Passable;
            }

            // Правило сервера: ломается всё непроходимое, кроме частей пака.
            if (!def.IsPassable && def.Outline is not (CellOutline.Wall or CellOutline.Corner or CellOutline.Door))
            {
                props |= CellConfigProperties.Breakable;
            }

            if (def.Glow > 0f)
            {
                props |= CellConfigProperties.Glowing;
            }

            configs[i] = new CellConfigurationPacket
            {
                Properties = props,
                // Протокол делит контур на искажение: волнистый двигает узлы,
                // гибкий нет, остальные держат.
                Distortion = def.Outline switch
                {
                    CellOutline.Wavy => CellDistortionType.Cause,
                    CellOutline.Pliant => CellDistortionType.Neutral,
                    _ => CellDistortionType.Block,
                },
                // Анимации сервера — со сдвигом фазы в кадр.
                Animation = def.AnimationType,
                AnimationSpeed = (byte)def.AnimationSpeed,
                FrameOffset = (byte)(def.AnimationType != CellAnimationType.None ? 1 : 0),
                ReliefGroup = def.RimMass,
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
            speeds[type] = (ushort)(def.IsPassable ? 20 : 100);
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
