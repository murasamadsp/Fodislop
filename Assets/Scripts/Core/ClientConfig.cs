#nullable enable

using System;

namespace Fodinae.Core
{
    [Serializable]
    public class ClientConfig
    {
        public float MasterVolume;
        public float SfxVolume;
        public float MusicVolume;
        public float AmbienceVolume;
        public float VoiceVolume;
        public float UiVolume;
        public float UiScale;
        public bool UseLight2D;
        public int GraphicsQuality;
        public float RenderScale;
        public int VSyncCount;
        public int AntiAliasing;

        public static ClientConfig Defaults { get; } = new()
        {
            MasterVolume = 1f,
            SfxVolume = 1f,
            MusicVolume = 0.5f,
            AmbienceVolume = 0.7f,
            VoiceVolume = 1f,
            UiVolume = 1f,
            UiScale = 1f,
            UseLight2D = true,
            GraphicsQuality = 2,
            RenderScale = 1f,
            VSyncCount = 1,
            AntiAliasing = 0,
        };
    }
}
