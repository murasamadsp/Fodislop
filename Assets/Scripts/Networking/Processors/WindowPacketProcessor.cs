#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;

namespace Fodinae.Networking.Processors;

public sealed class WindowPacketProcessor :
    IPacketProcessor<OpenWindowPacket>,
    IPacketProcessor<CloseWindowPacket>
{
    private readonly WindowCommandStream _commands;

    public WindowPacketProcessor(WindowCommandStream commands)
    {
        _commands = commands;
    }

    public void Process(OpenWindowPacket packet)
    {
        _commands.PublishOpen(packet);
    }

    public void Process(CloseWindowPacket packet)
    {
        _commands.PublishClose(packet);
    }

    public void Process(ModalWindowPacket packet)
    {
        _commands.PublishModal(packet);
    }
}
