#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
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

    public static void SetConfig(
        CellConfigurationPacket[] configs,
        CellType type,
        CellConfigProperties props,
        byte reliefGroup,
        int color = unchecked((int)0xFF808080),
        CellAnimationType animation = CellAnimationType.None,
        byte animationSpeed = 0,
        byte frameOffset = 0,
        CellDistortionType distortion = (CellDistortionType)0)
    {
        configs[(int)type] = new CellConfigurationPacket
        {
            Properties = props,
            ReliefGroup = reliefGroup,
            Color = color,
            Animation = animation,
            AnimationSpeed = animationSpeed,
            FrameOffset = frameOffset,
            Distortion = distortion,
        };
    }

    public static Dictionary<CellType, ushort> CreateMovementSpeeds(
        CellConfigurationPacket[] configurations)
    {
        var speeds = new Dictionary<CellType, ushort>(configurations.Length);
        for (int index = 0; index < configurations.Length; index++)
        {
            CellConfigurationPacket configuration = configurations[index];
            if (configuration.Properties == CellConfigProperties.None &&
                index != (int)CellType.Empty)
            {
                continue;
            }

            bool passable = (configuration.Properties & CellConfigProperties.Passable) != 0;
            speeds[(CellType)index] = (ushort)(passable ? 20 : 100);
        }

        return speeds;
    }
}
