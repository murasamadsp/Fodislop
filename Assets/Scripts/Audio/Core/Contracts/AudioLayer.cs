#nullable enable

using UnityEngine;

namespace Fodinae.Audio.Core;
public enum AudioBusType
{
    [Fodinae.Core.AudioBusPath("bus:/")]
    Master = 0,

    [Fodinae.Core.AudioBusPath("bus:/sfx")]
    SFX = 10,

    [Fodinae.Core.AudioBusPath("bus:/music")]
    Music = 20,

    [Fodinae.Core.AudioBusPath("bus:/voice")]
    Voice = 30,

    [Fodinae.Core.AudioBusPath("bus:/ambience")]
    Ambience = 40,

    [Fodinae.Core.AudioBusPath("bus:/ui")]
    UI = 50,
}

[System.Serializable]
public struct AudioLayer
{
    [Tooltip("Шина микшера: SFX, Music, Voice, Ambience, UI.")]
    public AudioBusType Bus;

    [Range(0f, 2f)]
    public float Volume;

    [Range(0.01f, 4f)]
    public float Pitch;

    [Tooltip("Пространственный звук: позиция передаётся в FMOD.")]
    public bool IsSpatial;

    public static AudioLayer SFXDefault() => new()
    {
        Bus = AudioBusType.SFX,
        Volume = 1f,
        Pitch = 1f,
        IsSpatial = true,
    };
    public static AudioLayer MusicDefault() => new()
    {
        Bus = AudioBusType.Music,
        Volume = 1f,
        Pitch = 1f,
        IsSpatial = false,
    };
}
