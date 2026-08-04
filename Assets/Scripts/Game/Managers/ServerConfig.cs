#nullable enable

using System;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    public class ServerConfig : MonoBehaviour, IServerConfig
    {
        private const string TAG = "[ServerConfig]";

        private float _digCooldown;
        private int _maxGlobalChatLength;
        private int _maxLocalChatLength;
        private bool _isInitialized;

        public float DigCooldown
        {
            get
            {
                EnsureInitialized();
                return _digCooldown;
            }
        }

        public int MaxGlobalChatLength
        {
            get
            {
                EnsureInitialized();
                return _maxGlobalChatLength;
            }
        }

        public int MaxLocalChatLength
        {
            get
            {
                EnsureInitialized();
                return _maxLocalChatLength;
            }
        }

        protected void Awake()
        {
            Debug.Log($"{TAG} Awake: waiting for server config...");
        }

        public void ApplyValues(float digCooldown, int maxGlobalChatLength, int maxLocalChatLength)
        {
            _digCooldown = digCooldown;
            _maxGlobalChatLength = maxGlobalChatLength;
            _maxLocalChatLength = maxLocalChatLength;
            _isInitialized = true;
            Debug.Log($"{TAG} Initialized from server: DigCooldown={DigCooldown}, MaxGlobalChat={MaxGlobalChatLength}, MaxLocalChat={MaxLocalChatLength}");
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException(
                    $"{TAG} Server config is not initialized. Call ApplyValues() before accessing config values.");
            }
        }
    }
}
