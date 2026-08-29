#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    /// <summary>
    /// ВНИМАНИЕ: Данный компонент является клиентской самодеятельностью (синтетической структурой).
    /// В протоколе Даркара (MinesServer.Networking) отдельного ServerConfigPacket не существует.
    /// Передать тимлиду / бэкенду для согласования: либо добавить серверный пакет параметров мира,
    /// либо упразднить данный менеджер и брать лимиты из ClientConfig/констант протокола.
    /// </summary>
    public class ServerConfig : MonoBehaviour, IServerConfig
    {
        private const string TAG = "[ServerConfig]";

        private float _digCooldown = ProjectRuntimeContracts.Gameplay.DefaultDigCooldown;
        private int _maxGlobalChatLength = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;
        private int _maxLocalChatLength = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;
        private bool _isInitialized = true;

        public bool IsInitialized => _isInitialized;

        public event Action? OnInitialized;

        public float DigCooldown => _digCooldown;
        public int MaxGlobalChatLength => _maxGlobalChatLength;
        public int MaxLocalChatLength => _maxLocalChatLength;

        public void ApplyValues(float digCooldown, int maxGlobalChatLength, int maxLocalChatLength)
        {
            _digCooldown = digCooldown;
            _maxGlobalChatLength = maxGlobalChatLength;
            _maxLocalChatLength = maxLocalChatLength;
            _isInitialized = true;
            OnInitialized?.Invoke();
            Debug.Log($"{TAG} Updated values: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }
    }
}
