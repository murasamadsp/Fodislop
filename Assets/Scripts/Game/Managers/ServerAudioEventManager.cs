#nullable enable

using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerAudioEventManager : MonoBehaviour, IServerAudioService
    {
        private const string TAG = "[ServerAudioEventManager]";
        private readonly List<ServerAudioEvent> _activeEffects = new();

        public void PlayEffect(AudioPacket packet)
        {
            var vfxType = MapAudioToVFX(packet.EffectType);
            var slot = ServiceLocator.Resolve<VFXPool>() != null ? ServiceLocator.Resolve<VFXPool>().Acquire(vfxType) : null;

            var effect = new ServerAudioEvent(packet, slot);
            _activeEffects.Add(effect);
        }

        private static VFXType MapAudioToVFX(global::MinesServer.Data.SFX audioType)
        {
            // Enum is logically fixed on client, but server can extend it at any time.
            // Unknown values must NOT be silently dropped — they should flow through
            // as Custom so client can request/display them by numeric id rather than
            // treating them as "no effect".
            return audioType switch
            {
                global::MinesServer.Data.SFX.Bz => VFXType.Bz,
                global::MinesServer.Data.SFX.Destroy => VFXType.Destroy,
                global::MinesServer.Data.SFX.Death => VFXType.Death,
                _ => VFXType.Custom,
            };
        }

        public void ClearAllEffects()
        {
            int count = _activeEffects.Count;
            foreach (var effect in _activeEffects)
            {
                effect.Dispose();
            }

            _activeEffects.Clear();
            if (count > 0)
            {
                Debug.Log($"{TAG} Cleared {count} active effects");
            }
        }

        protected void OnDestroy()
        {
            ClearAllEffects();
        }

        protected void Update()
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
    }
}
