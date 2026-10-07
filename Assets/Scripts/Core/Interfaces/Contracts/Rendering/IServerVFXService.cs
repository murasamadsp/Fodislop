#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;

public interface IServerVFXService
{
    void PlayEffect(VFX effectType, ushort x, ushort y, ushort targetBotId = 0, int param = 0);
    void PlayEffect(VFXPacket packet);
}
