#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    public class ServerAudioEventManager : MonoBehaviour, IServerAudioService
    {
        private const string TAG = "[ServerAudioEventManager]";
        private readonly List<ServerAudioEvent> _activeEffects = new();

        [Inject]
        private IVFXService _vfxService = null!;

        [Inject]
        private IRobotService _robotService = null!;

        [Inject]
        private IAudioSystem _audioSystem = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private VFXPool _vfxPool = null!;

        public void PlayEffect(AudioPacket packet)
        {
            if (packet.EffectType == global::MinesServer.Data.SFX.Music)
            {
                _audioSystem.Play2D("music/evil_huge", AudioLayer.MusicDefault());
                return;
            }

            var vfxType = MapAudioToVFX(packet.EffectType);
            IVFXSlot? slot = _vfxService.Acquire(vfxType);

            var effect = new ServerAudioEvent(
                packet,
                slot,
                _robotService,
                _audioSystem,
                _assetLoader,
                _mapManager,
                _vfxPool);
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
