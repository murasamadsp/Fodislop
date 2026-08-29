#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Networking.Processors;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

namespace Fodinae.Tests.Networking
{
    [TestFixture]
    public class ClanProcessorTests
    {
        private PlayerStatsModel _stats = null!;
        private ClanProcessor _processor = null!;

        [SetUp]
        public void SetUp()
        {
            _stats = new PlayerStatsModel();
            _processor = new ClanProcessor(_stats);
        }

        [Test]
        public void Process_ShowClanPacket_SetsClanIdInPlayerStats()
        {
            var packet = new ShowClanPacket(777);
            _processor.Process(packet);

            Assert.AreEqual(777, _stats.ClanId);
        }

        [Test]
        public void Process_HideClanPacket_ResetsClanIdToZero()
        {
            _stats.SetClanId(777);
            Assert.AreEqual(777, _stats.ClanId);

            var packet = new HideClanPacket();
            _processor.Process(packet);

            Assert.AreEqual(0, _stats.ClanId);
        }
    }
}
