#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        private readonly IServerAudioService _audio;

        public AudioPacketProcessor(IServerAudioService audio)
        {
            _audio = audio;
        }

        public void Process(AudioPacket packet)
        {
            _audio.PlayEffect(packet);
        }
    }
}
