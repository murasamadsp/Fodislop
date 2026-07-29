#nullable enable

using Fodinae.Scripts.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerConfig : MonoBehaviour, IServerConfig
    {
        private const string TAG = "[ServerConfig]";

        public float DigCooldown { get; private set; } = 0.3f;
        public int MaxGlobalChatLength { get; private set; } = 50;
        public int MaxLocalChatLength { get; private set; } = 20;

        protected void Awake()
        {
            Debug.Log($"{TAG} Initialized: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }
    }
}
