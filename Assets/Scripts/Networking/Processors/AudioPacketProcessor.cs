#nullable enable

using Fodinae.Core;
using Fodinae.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        public void Process(AudioPacket packet)
        {
            if (Fodinae.Core.ServiceLocator.Resolve<ServerAudioEventManager>() != null)
            {
                Fodinae.Core.ServiceLocator.Resolve<ServerAudioEventManager>().PlayEffect(packet);
            }
        }
    }
}
