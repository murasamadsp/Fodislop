#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Connection.Client;

internal static class DummyCellConfigurationUtilities
{
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
