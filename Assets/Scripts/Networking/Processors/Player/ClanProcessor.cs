#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Kern.Networking.Processors;

public sealed class ClanProcessor :
    IPacketProcessor<ShowClanPacket>,
    IPacketProcessor<HideClanPacket>
{
    private readonly IPlayerStats _stats;
    public ClanProcessor(IPlayerStats stats)
    {
        _stats = stats;
    }

    public void Process(ShowClanPacket packet) => _stats.SetClanId(packet.ClanId);

    public void Process(HideClanPacket packet) => _stats.SetClanId(0);
}
