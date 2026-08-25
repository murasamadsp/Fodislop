#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
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
