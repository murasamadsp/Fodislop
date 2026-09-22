#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class VfxPacketProcessor(IServerVfxService vfx) : IPacketProcessor<VFXPacket>
{
    public void Process(VFXPacket packet) => vfx.PlayEffect(packet);
}
