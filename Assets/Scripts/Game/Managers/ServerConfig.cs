#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    public class ServerConfig : MonoBehaviour, IServerConfig
    {
        private const string TAG = "[ServerConfig]";

        public float DigCooldown { get; private set; }
        public int MaxGlobalChatLength { get; private set; }
        public int MaxLocalChatLength { get; private set; }

        protected void Awake()
        {
            DigCooldown = PlayerPrefs.GetFloat(nameof(DigCooldown), 0.3f);
            MaxGlobalChatLength = PlayerPrefs.GetInt(nameof(MaxGlobalChatLength), 50);
            MaxLocalChatLength = PlayerPrefs.GetInt(nameof(MaxLocalChatLength), 20);
            Debug.Log($"{TAG} Initialized: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }
    }
}
