using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Scripts.Networking.Processors
{
    /// <summary>
    /// Decoupled SOLID Processor for Server Audio & SFX Event Packets.
    /// Dispatches server sound triggers to ServerAudioEventManager for FMOD & Effekseer playback.
    /// </summary>
    public class SFXPacketProcessor : IPacketProcessor<SFXPacket>
    {
        public void Process(SFXPacket packet)
        {
            if (ServerAudioEventManager.Instance != null)
            {
                ServerAudioEventManager.Instance.PlayEffect(packet);
            }
        }
    }
}
