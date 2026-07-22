using MinesServer.Networking.Server.Packets;

namespace Fodinae.Scripts.Networking.Processors
{
    /// <summary>
    /// SOLID Single Responsibility Interface for processing specific server packets.
    /// Replaces monolithic HandleXxxPacket switch/method chains in PacketHandler.
    /// </summary>
    /// <typeparam name="T">Type of ServerPacket payload to process.</typeparam>
    public interface IPacketProcessor<in T>
    {
        void Process(T packet);
    }
}
