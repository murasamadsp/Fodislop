#nullable enable

using MinesServer.Networking.Server.Packets;

namespace Fodinae.Networking.Processors;
/// <typeparam name="T">Type of ServerPacket payload to process.</typeparam>
public interface IPacketProcessor<in T>
{
    void Process(T packet);
}
