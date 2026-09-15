#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors;

public sealed class AudioPacketProcessor(IServerAudioService audio) : IPacketProcessor<AudioPacket>
{
    public void Process(AudioPacket packet) => audio.PlayEffect(packet);
}
