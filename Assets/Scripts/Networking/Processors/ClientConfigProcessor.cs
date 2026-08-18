#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Networking.Processors
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
            Debug.Log($"[ClientConfig] Received: master={packet.SoundConfig.Master}, sounds={packet.SoundConfig.IndividualSounds.Count}, keybinds={packet.Keybinds.Count}");
            var audio = Fodinae.Core.ServiceLocator.Resolve<IAudioSystem>();
            IClientConfigManager configManager = Fodinae.Core.ServiceLocator.Resolve<IClientConfigManager>() ??
                throw new InvalidOperationException("Client config manager is required before processing ClientConfigPacket.");
            ClientConfig clientConfig = configManager.Config ??
                throw new InvalidOperationException("Client config must be initialized before processing ClientConfigPacket.");
            if (audio != null)
            {
                float masterVol = packet.SoundConfig.Master / 255f;
                audio.SetBusVolume(AudioBusType.Master, masterVol);
                clientConfig.MasterVolume = masterVol;

                foreach (var kv in packet.SoundConfig.IndividualSounds)
                {
                    string key = kv.Key.ToLowerInvariant();
                    if (SoundKeyToBus.TryGetValue(key, out var bus))
                    {
                        float vol = kv.Value / 255f;
                        audio.SetBusVolume(bus, vol);
                        switch (bus)
                        {
                            case AudioBusType.SFX:
                                clientConfig.SfxVolume = vol;
                                break;
                            case AudioBusType.Music:
                                clientConfig.MusicVolume = vol;
                                break;
                            case AudioBusType.Voice:
                                clientConfig.VoiceVolume = vol;
                                break;
                            case AudioBusType.Ambience:
                                clientConfig.AmbienceVolume = vol;
                                break;
                            case AudioBusType.UI:
                                clientConfig.UiVolume = vol;
                                break;
                        }
                    }
                }

                configManager.Save();
            }

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
