#nullable enable

using System;
using System.Collections.Generic;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Information;

namespace Kern.Networking.Processors;

public sealed class ClientConfigProcessor(
    ITextureStorageService textureStorage,
    IAsyncOperationSupervisor operations,
    IAudioSystem? audio = null,
    IClientConfigManager? clientConfig = null) : IPacketProcessor<ClientConfigPacket>
{
    private static readonly Dictionary<string, AudioBusType> s_soundKeyToBus = new(StringComparer.OrdinalIgnoreCase)
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
        ApplySoundConfig(packet.SoundConfig);

        if (packet.UnrenderedTextures != null && packet.UnrenderedTextures.Count > 0)
        {
            operations.Run(
                "preload_textures",
                cancellationToken => textureStorage.PreloadTexturesAsync(packet.UnrenderedTextures, cancellationToken));
        }
    }

    private void ApplySoundConfig(SoundConfigPacket soundConfig)
    {
        if (audio == null)
        {
            return;
        }

        float masterVol = soundConfig.Master / 255f;
        audio.SetBusVolume(AudioBusType.Master, masterVol);

        if (clientConfig != null)
        {
            clientConfig.UpdateSection(
                config => config.Audio,
                settings =>
                {
                    settings.MasterVolume = masterVol;
                    if (soundConfig.IndividualSounds != null)
                    {
                        foreach (var kv in soundConfig.IndividualSounds)
                        {
                            if (s_soundKeyToBus.TryGetValue(kv.Key, out AudioBusType bus))
                            {
                                float vol = kv.Value / 255f;
                                SetBusSetting(settings, bus, vol);
                            }
                        }
                    }
                });
        }

        if (soundConfig.IndividualSounds != null)
        {
            foreach (var kv in soundConfig.IndividualSounds)
            {
                if (s_soundKeyToBus.TryGetValue(kv.Key, out AudioBusType bus))
                {
                    float vol = kv.Value / 255f;
                    audio.SetBusVolume(bus, vol);
                }
            }
        }
    }

    private static void SetBusSetting(AudioSettings settings, AudioBusType bus, float volume)
    {
        switch (bus)
        {
            case AudioBusType.Master:
                settings.MasterVolume = volume;
                break;
            case AudioBusType.SFX:
                settings.SfxVolume = volume;
                break;
            case AudioBusType.Music:
                settings.MusicVolume = volume;
                break;
            case AudioBusType.Ambience:
                settings.AmbienceVolume = volume;
                break;
            case AudioBusType.Voice:
                settings.VoiceVolume = volume;
                break;
            case AudioBusType.UI:
                settings.UIVolume = volume;
                break;
            default:
                break;
        }
    }
}
