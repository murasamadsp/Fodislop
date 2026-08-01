#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        public void Process(AudioPacket packet)
        {
            var mgr = ServiceLocator.Resolve<IServerAudioService>();
            mgr?.PlayEffect(packet);
        }
    }
}
