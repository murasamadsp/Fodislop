#nullable enable

using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Core.Interfaces
{
    public interface IServerAudioService
    {
        void PlayEffect(AudioPacket packet);
        void ClearAllEffects();
    }
}
