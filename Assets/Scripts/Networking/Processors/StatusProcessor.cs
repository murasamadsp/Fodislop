#nullable enable

using System.Linq;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using UnityEngine;

namespace Fodinae.Networking.Processors
{
    public class StatusProcessor : IPacketProcessor<OnlinePacket>, IPacketProcessor<PingPacket>, IPacketProcessor<OutdatedClientPacket>, IPacketProcessor<AddStatusLinePacket>, IPacketProcessor<ClearStatusLinePacket>, IPacketProcessor<ClearStatusPacket>
    {
        private readonly IPlayerStats _stats;
        private readonly NetworkStatusModel _statusModel;
        private readonly INetworkService _networkService;
        private readonly ILocalizationService? _loc;
        private bool _outdatedClientHandled;

        public StatusProcessor(IPlayerStats stats, NetworkStatusModel statusModel, INetworkService networkService, ILocalizationService? loc = null)
        {
            _stats = stats;
            _statusModel = statusModel;
            _networkService = networkService;
            _loc = loc;
        }

        public void Process(OnlinePacket packet)
        {
            _statusModel.SetOnline((int)packet.Players, (int)packet.Programmator);
        }

        public void Process(PingPacket packet)
        {
            _statusModel.SetPing(packet.PreviousPing);

            var networkService = _networkService;
            networkService?.Send(new PongPacket(packet.SentAt));
        }

        public void Process(OutdatedClientPacket packet)
        {
            if (_outdatedClientHandled)
            {
                return;
            }

            _outdatedClientHandled = true;

            // Description приходит от сервера свободным текстом; известные
            // клиентские причины передаются ключами словаря — резолвим их.
            string description = _loc != null && _loc.HasKey(packet.Description)
                ? _loc.Get(packet.Description)
                : packet.Description;
            string detail = _loc != null
                ? _loc.Get("network.error.outdated", packet.Name, description, packet.UpdateURL)
                : $"Версия: {packet.Name}\n{description}\nСкачать: {packet.UpdateURL}";
            Debug.LogWarning($"[StatusProcessor] Клиент устарел: {detail}");
            if (!string.IsNullOrWhiteSpace(packet.UpdateURL))
            {
                Application.OpenURL(packet.UpdateURL);
            }
        }

        public void Process(AddStatusLinePacket packet)
        {
            var stats = _stats;
            if (stats == null)
            {
                Debug.LogWarning("[StatusProcessor] IPlayerStats не зарегистрирован — статус-линия не добавлена");
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
            var stats = _stats;
            stats?.RemoveStatusLine(packet.Tag);
        }

        public void Process(ClearStatusPacket packet)
        {
            var stats = _stats;
            stats?.ClearStatusLines();
        }
    }
}
