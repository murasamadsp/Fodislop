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

    public static int GetCrystalBasketIndex(CellType cell) =>
        BlockRegistry.Get(cell).CrystalBasketIndex;

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

            if (def.Breakable)
            {
                props |= CellConfigProperties.Breakable;
            }

            if (def.CastsShadow)
            {
                props |= CellConfigProperties.DropsShadow;
            }

            if (def.ReceivesShadow)
            {
                props |= CellConfigProperties.ReceivesShadow;
            }

            if (def.BlendWithNeighbors)
            {
                props |= CellConfigProperties.Blending;
            }

            if (def.EmitsLight)
            {
                props |= CellConfigProperties.Glowing;
            }

            int color = DummyMapColors.Get(i);
            if (!string.IsNullOrWhiteSpace(def.MapColorHex) &&
                !string.Equals(def.MapColorHex, "Auto", StringComparison.OrdinalIgnoreCase) &&
                UnityEngine.ColorUtility.TryParseHtmlString(def.MapColorHex, out UnityEngine.Color parsedColor))
            {
                UnityEngine.Color32 c32 = (UnityEngine.Color32)parsedColor;
                color = unchecked((int)(((uint)c32.a << 24) | ((uint)c32.r << 16) | ((uint)c32.g << 8) | c32.b));
            }

            configs[i] = new CellConfigurationPacket
            {
                Properties = props,
                Distortion = def.MeshDistortion,
                Animation = def.ShaderEffect,
                AnimationSpeed = def.ShaderEffectSpeed,
                FrameOffset = def.ShaderEffectPhaseOffset,
                ReliefGroup = def.TerrainSeamGroupId,
                Color = color,
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
            ushort speed = def.MoveCooldownMs > 0
                ? def.MoveCooldownMs
                : (ushort)(def.Passable ? 20 : 100);

            speeds[type] = speed;
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
