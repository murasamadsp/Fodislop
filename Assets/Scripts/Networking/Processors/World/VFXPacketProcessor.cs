#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class VFXPacketProcessor : IPacketProcessor<VFXPacket>
{
    private readonly IServerVFXService _vfx;
    private readonly ILocalPlayerState _localPlayer;
    public VFXPacketProcessor(
    IServerVFXService vfx,
    ILocalPlayerState localPlayer)
    {
        _vfx = vfx;
        _localPlayer = localPlayer;
    }

    public void Process(VFXPacket packet)
    {
        if (packet.EffectType == VFX.Bz)
        {
            _localPlayer.Current?.ConfirmDigAction(packet.X, packet.Y);
        }

        _vfx.PlayEffect(packet);
    }
}
