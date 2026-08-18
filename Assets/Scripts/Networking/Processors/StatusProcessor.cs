#nullable enable

using System.Linq;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.UI;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class StatusProcessor : IPacketProcessor<OnlinePacket>, IPacketProcessor<PingPacket>, IPacketProcessor<OutdatedClientPacket>, IPacketProcessor<AddStatusLinePacket>, IPacketProcessor<ClearStatusLinePacket>, IPacketProcessor<ClearStatusPacket>
    {
        public void Process(OnlinePacket packet)
        {
            var fps = Fodinae.Core.ServiceLocator.Resolve<FPSCounter>();
            if (fps != null)
            {
                fps.SetOnline((int)packet.Players, (int)packet.Programmator);
            }
        }

        public void Process(PingPacket packet)
        {
            var fps = Fodinae.Core.ServiceLocator.Resolve<FPSCounter>();
            if (fps != null)
            {
                fps.SetPing(packet.PreviousPing);
            }

            var networkService = Fodinae.Core.ServiceLocator.Resolve<INetworkService>();
            networkService?.Send(new PongPacket(packet.SentAt));
        }

        public void Process(OutdatedClientPacket packet)
        {
            string detail = $"\u0412\u0435\u0440\u0441\u0438\u044f: {packet.Name}\n{packet.Description}\n\u0421\u043a\u0430\u0447\u0430\u0442\u044c: {packet.UpdateURL}";
            Debug.LogError($"[StatusProcessor] \u041a\u043b\u0438\u0435\u043d\u0442 \u0443\u0441\u0442\u0430\u0440\u0435\u043b: {packet.Name}");
            GameErrorUI.ReportFatal(detail);
            Application.OpenURL(packet.UpdateURL);
        }

        public void Process(AddStatusLinePacket packet)
        {
            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
            if (stats == null)
            {
                GameErrorUI.ReportError("IPlayerStats не зарегистрирован — статус-линия не добавлена");
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
            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.RemoveStatusLine(packet.Tag);
        }

        public void Process(ClearStatusPacket packet)
        {
            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
            stats?.ClearStatusLines();
        }
    }
}
