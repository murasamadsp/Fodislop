#nullable enable

using System;

namespace Fodinae.Core
{
    /// <summary>
    /// Единственный источник дефолтов для клиентского конфига.
    /// Все значения здесь — канонические, менять их здесь, а не в разбросанных по коду fallback-значениях.
    /// </summary>
    [Serializable]
    public class ClientConfig
    {
        public float MasterVolume { get; set; }
        public float SfxVolume { get; set; }
        public float MusicVolume { get; set; }
        public float AmbienceVolume { get; set; }
        public float VoiceVolume { get; set; }
        public float UiVolume { get; set; }
        public float UiScale { get; set; }
        public bool UseLight2D { get; set; }
        public int GraphicsQuality { get; set; }
        public float RenderScale { get; set; }
        public int VSyncCount { get; set; }
        public int AntiAliasing { get; set; }

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
