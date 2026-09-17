#nullable enable

using Darkar25.Fodina.World.Network.Packets;

namespace MinesServer.Networking.Connection.Client;

internal readonly record struct DummyWorldDescriptor(
    int Width,
    int Height,
    CellConfigurationPacket[] CellConfigurations);
