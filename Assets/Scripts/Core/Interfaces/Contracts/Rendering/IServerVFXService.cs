#nullable enable

using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;

public interface IServerVFXService
{
    void PlayEffect(VFXPacket packet);
}
