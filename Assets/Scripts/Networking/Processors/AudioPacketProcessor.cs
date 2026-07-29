#nullable enable

using Fodinae.Scripts.Core;
using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Scripts.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        public void Process(AudioPacket packet)
        {
            if (Fodinae.Scripts.Core.ServiceLocator.Resolve<ServerAudioEventManager>() != null)
            {
                Fodinae.Scripts.Core.ServiceLocator.Resolve<ServerAudioEventManager>().PlayEffect(packet);
            }
        }
    }
}
