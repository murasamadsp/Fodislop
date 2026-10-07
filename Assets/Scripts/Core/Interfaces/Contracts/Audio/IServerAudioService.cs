#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;

public interface IServerAudioService
{
    void PlayEffect(SFX effectType, ushort x, ushort y, ushort targetBotId = 0, int param = 0);
    void PlayEffect(AudioPacket packet);
    void ClearAllEffects();
}
