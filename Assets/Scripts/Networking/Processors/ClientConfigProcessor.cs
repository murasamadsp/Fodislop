#nullable enable

using System.Collections.Generic;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Processors
{
    public class ClientConfigProcessor : IPacketProcessor<ClientConfigPacket>
    {
        private static readonly Dictionary<string, AudioBusType> SoundKeyToBus = new()
        {
            ["master"] = AudioBusType.Master,
            ["sfx"] = AudioBusType.SFX,
            ["music"] = AudioBusType.Music,
            ["voice"] = AudioBusType.Voice,
            ["ambience"] = AudioBusType.Ambience,
            ["ui"] = AudioBusType.UI,
        };

        public void Process(ClientConfigPacket packet)
        {
            Debug.Log($"[ClientConfig] Received: master={packet.SoundConfig.Master}, renderer={packet.Renderer}, sounds={packet.SoundConfig.IndividualSounds.Count}, keybinds={packet.Keybinds.Count}");
            var audio = Fodinae.Scripts.Core.ServiceLocator.Resolve<IAudioSystem>();
            if (audio != null)
            {
                float masterVol = packet.SoundConfig.Master / 255f;
                audio.SetBusVolume(AudioBusType.Master, masterVol);
                PlayerPrefs.SetFloat("Audio_Master", masterVol);

                foreach (var kv in packet.SoundConfig.IndividualSounds)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (SoundKeyToBus.TryGetValue(key, out var bus))
                    {
                        float vol = kv.Value / 255f;
                        audio.SetBusVolume(bus, vol);
                        PlayerPrefs.SetFloat($"Audio_{bus}", vol);
                    }
                }
            }

            var terrain = TerrainRenderer.Instance;
            if (terrain != null)
            {
                bool simple = packet.Renderer switch
                {
                    RendererMode.Simplified => true,
                    _ => false,
                };
                terrain.SetSimpleGraphics(simple);
                PlayerPrefs.SetInt("SimpleGraphics", simple ? 1 : 0);
            }

            PlayerPrefs.Save();

            if (packet.Keybinds.Count > 0)
            {
                Debug.Log($"[ClientConfig] Received {packet.Keybinds.Count} keybinds");
            }

            if (packet.UnrenderedTextures.Count > 0)
            {
                Debug.Log($"[ClientConfig] Received {packet.UnrenderedTextures.Count} unrendered textures");
            }
        }
    }
}
