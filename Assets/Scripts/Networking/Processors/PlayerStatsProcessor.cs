#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;

namespace Fodinae.Networking.Processors
{
    public class PlayerStatsProcessor : IPacketProcessor<LevelPacket>, IPacketProcessor<HealthPacket>, IPacketProcessor<CurrencyPacket>, IPacketProcessor<GeologyPacket>, IPacketProcessor<BasketPacket>, IPacketProcessor<MaxDepthPacket>, IPacketProcessor<DailyBonusStatePacket>, IPacketProcessor<SkillProgressPacket>
    {
        private readonly IPlayerStats _stats;

        public PlayerStatsProcessor(IPlayerStats stats)
        {
            _stats = stats;
        }

        public void Process(LevelPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetLevel(packet.Level);
            }
        }

        public void Process(HealthPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetHealth(packet.Current, packet.Max);
            }
        }

        public void Process(CurrencyPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetCurrency(packet.Money, packet.Creds);
            }
        }

        public void Process(GeologyPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);
            }
        }

        public void Process(BasketPacket packet)
        {
            // The [Probe] log that stood here fired on every BasketPacket -
            // several times a second, forever. The other [Probe] logs are
            // one-shot load timings; this one was in a per-packet path, and in
            // the editor every Debug.Log captures and formats a managed stack
            // trace, adds a console entry and writes to Editor.log. 531 of them
            // in the current session log.
            var s = _stats;
            if (s != null)
            {
                s.SetBasket(packet.Capacity, packet.Contents);
            }
        }

        public void Process(MaxDepthPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetMaxDepth(packet.Depth);
            }
        }

        public void Process(DailyBonusStatePacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetDailyBonusAvailable(packet.Enabled);
            }
        }

        public void Process(SkillProgressPacket packet)
        {
            var s = _stats;
            if (s != null)
            {
                s.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
            }
        }
    }
}
