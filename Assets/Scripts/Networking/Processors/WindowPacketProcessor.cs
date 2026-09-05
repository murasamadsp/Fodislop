#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;

namespace Fodinae.Networking.Processors;

public sealed class WindowPacketProcessor(WindowCommandStream commands) :
    IPacketProcessor<OpenWindowPacket>,
    IPacketProcessor<CloseWindowPacket>
{
    public void Process(OpenWindowPacket packet) => commands.PublishOpen(packet);

    public void Process(CloseWindowPacket packet) => commands.PublishClose(packet);

    public void Process(ModalWindowPacket packet) => commands.PublishModal(packet);
}
