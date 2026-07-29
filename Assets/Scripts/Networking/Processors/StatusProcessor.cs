#nullable enable

using System.Linq;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.UI;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class StatusProcessor : IPacketProcessor<OnlinePacket>, IPacketProcessor<PingPacket>, IPacketProcessor<OutdatedClientPacket>, IPacketProcessor<AddStatusLinePacket>, IPacketProcessor<ClearStatusLinePacket>, IPacketProcessor<ClearStatusPacket>
    {
        public void Process(OnlinePacket packet)
        {
            var fps = Fodinae.Scripts.Core.ServiceLocator.Resolve<FPSCounter>();
            if (fps != null)
            {
                fps.SetOnline((int)packet.Players, (int)packet.Programmator);
            }
        }

        public void Process(PingPacket packet)
        {
            var fps = Fodinae.Scripts.Core.ServiceLocator.Resolve<FPSCounter>();
            if (fps != null)
            {
                fps.SetPing(packet.PreviousPing);
            }

            var networkService = Fodinae.Scripts.Core.ServiceLocator.Resolve<INetworkService>();
            networkService?.Send(new PongPacket(packet.SentAt));
        }

        public void Process(OutdatedClientPacket packet)
        {
            Debug.LogError($"[StatusProcessor] Клиент устарел: {packet.Name}");
            Debug.LogError($"[StatusProcessor] {packet.Description}");
            Debug.LogError($"[StatusProcessor] Скачать: {packet.UpdateURL}");
            Application.OpenURL(packet.UpdateURL);
        }

        public void Process(AddStatusLinePacket packet)
        {
            var stats = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (stats == null)
            {
                return;
            }

            var sysColor = packet.Color;
            var unityColor = new Color(sysColor.R / 255f, sysColor.G / 255f, sysColor.B / 255f, sysColor.A / 255f);
            long expiry = 0;
            if (packet.Text.Count > 1)
            {
                long.TryParse(packet.Text[1], out expiry);
            }

            stats.AddStatusLine(packet.Tag, packet.Text.ToArray(), unityColor, packet.BlinkRate, expiry);
        }

        public void Process(ClearStatusLinePacket packet)
        {
            var stats = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.RemoveStatusLine(packet.Tag);
        }

        public void Process(ClearStatusPacket packet)
        {
            var stats = Fodinae.Scripts.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.ClearStatusLines();
        }
    }
}
