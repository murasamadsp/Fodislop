#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Networking.Processors;
using Fodinae.UI;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.Networking
{
    [TestFixture]
    public class StatusProcessorTests
    {
        private PlayerStatsModel _stats = null!;
        private StatusProcessor _processor = null!;
        private GameObject _fpsGo = null!;
        private FPSCounter _fpsCounter = null!;
        private StubNetworkService _networkService = null!;

        [SetUp]
        public void SetUp()
        {
            _stats = new PlayerStatsModel();
            _fpsGo = new GameObject("TestFPSCounter");
            _fpsCounter = _fpsGo.AddComponent<FPSCounter>();
            _networkService = new StubNetworkService();

            _processor = new StatusProcessor(_stats, _fpsCounter, _networkService);
        }

        [TearDown]
        public void TearDown()
        {
            if (_fpsGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_fpsGo);
            }
        }

        [Test]
        public void Process_OnlinePacket_UpdatesOnlineCounters()
        {
            var packet = new OnlinePacket(120, 15);
            _processor.Process(packet);

            Assert.AreEqual(120, _fpsCounter.OnlinePlayers);
            Assert.AreEqual(15, _fpsCounter.OnlineProgrammator);
        }

        [Test]
        public void Process_PingPacket_UpdatesPingAndRepliesWithPong()
        {
            var packet = new PingPacket(45, 123456789);
            _processor.Process(packet);

            Assert.AreEqual(45, _fpsCounter.PingMs);
            Assert.IsTrue(_networkService.SentPong, "PingPacket must be answered with a PongPacket.");
        }

        private sealed class StubNetworkService : INetworkService
        {
            public bool SentPong { get; private set; }

            public void Subscribe<T>(Action<T> handler)
            {
            }

            public void Unsubscribe<T>(Action<T> handler)
            {
            }

            public void SendAction(IActionClientPacket action)
            {
            }

            public void Send(IRootClientPacket packet)
            {
                if (packet is PongPacket)
                {
                    SentPong = true;
                }
            }
        }

        [Test]
        public void Process_AddStatusLinePacket_AddsStatusLineToStats()
        {
            var packet = new AddStatusLinePacket(
                0,
                System.Drawing.Color.Green,
                "buff_speed",
                ["Speed +20%", "60s"]);

            _processor.Process(packet);

            Assert.IsTrue(_stats.StatusLines.ContainsKey("buff_speed"));
            var line = _stats.StatusLines["buff_speed"];
            Assert.AreEqual("Speed +20%", line.Text[0]);
        }

        [Test]
        public void Process_ClearStatusLinePacket_RemovesStatusLine()
        {
            _stats.AddStatusLine("buff_speed", ["Speed +20%"], Color.green, 0, 0);
            Assert.AreEqual(1, _stats.StatusLines.Count);

            var packet = new ClearStatusLinePacket("buff_speed");
            _processor.Process(packet);

            Assert.IsFalse(_stats.StatusLines.ContainsKey("buff_speed"));
        }

        [Test]
        public void Process_ClearStatusPacket_ClearsAllStatusLines()
        {
            _stats.AddStatusLine("buff1", ["Buff 1"], Color.green, 0, 0);
            _stats.AddStatusLine("buff2", ["Buff 2"], Color.blue, 0, 0);
            Assert.AreEqual(2, _stats.StatusLines.Count);

            var packet = default(ClearStatusPacket);
            _processor.Process(packet);

            Assert.AreEqual(0, _stats.StatusLines.Count);
        }
    }
}
