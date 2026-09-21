#nullable enable

using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;

public interface IServerVfxService
{
    void PlayEffect(VFXPacket packet);
}
