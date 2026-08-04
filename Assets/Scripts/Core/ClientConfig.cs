#nullable enable

using System;

namespace Fodinae.Core
{
    [Serializable]
    public class ClientConfig
    {
        public float MasterVolume { get; set; } = 1f;
        public float SfxVolume { get; set; } = 1f;
        public float MusicVolume { get; set; } = 0.5f;
        public float AmbienceVolume { get; set; } = 0.7f;
        public float VoiceVolume { get; set; } = 1f;
        public float UiVolume { get; set; } = 1f;
        public float UiScale { get; set; } = 1f;
        public bool UseLight2D { get; set; } = true;
        public int GraphicsQuality { get; set; } = 2;
        public float RenderScale { get; set; } = 1f;
        public int VSyncCount { get; set; } = 1;
        public int AntiAliasing { get; set; } = 0;
    }
}
