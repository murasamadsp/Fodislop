using System.Collections.Generic;
using Fodinae.Scripts.Audio;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    /// <summary>
    /// Singleton manager for server-driven SFX audio+visual effects.
    /// Receives SFXPackets, acquires a <see cref="SFXPool.PooledSlot"/>
    /// from <see cref="SFXPool"/>, and spawns <see cref="SFXEffectInstance"/> objects
    /// that load and play visual+audio assets at world positions.
    /// Follows the same singleton pattern as RobotManager and PackManager.
    /// </summary>
    public class SFXEffectManager : MonoBehaviour
    {
        private static SFXEffectManager _instance;
        private static bool _isQuitting = false;

        private readonly List<SFXEffectInstance> _activeEffects = new();

        public static SFXEffectManager InstanceIfExists => _instance;

        public static SFXEffectManager Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SFXEffectManager>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[SFXEffectManager]");
                        _instance = go.AddComponent<SFXEffectManager>();

                        if (Application.isPlaying)
                        {
                            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                            DontDestroyOnLoad(parent);
                            go.transform.SetParent(parent.transform);
                        }
                    }
                }

                return _instance;
            }
        }

        /// <summary>
        /// Spawn a new combined audio+visual effect from an incoming SFXPacket.
        /// Acquires a pooled slot from <see cref="SFXPool"/> and creates an
        /// <see cref="SFXEffectInstance"/> that manages its lifecycle.
        /// </summary>
        public void PlayEffect(SFXPacket packet)
        {
            var slot = SFXPool.Instance?.Acquire(packet.EffectType);
            if (slot == null)
            {
                return;
            }

            var effect = new SFXEffectInstance(packet, slot);
            _activeEffects.Add(effect);
        }

        /// <summary>
        /// Dispose all active effects. Called on world reset.
        /// </summary>
        public void ClearAllEffects()
        {
            foreach (var effect in _activeEffects)
            {
                effect.Dispose();
            }

            _activeEffects.Clear();
        }

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                DontDestroyOnLoad(parent);
                transform.SetParent(parent.transform);
            }

            _isQuitting = false;
        }

        protected virtual void Update()
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                effect.Update();
                if (effect.IsDisposed)
                {
                    _activeEffects.RemoveAt(i);
                }
            }
        }

        protected virtual void OnDestroy()
        {
            ClearAllEffects();
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
            ClearAllEffects();
        }
    }
}
