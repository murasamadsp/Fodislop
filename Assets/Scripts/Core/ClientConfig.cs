#nullable enable

using System;

namespace Fodinae.Core
{
    [Serializable]
    public class ClientConfig
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;
        public float MasterVolume;
        public float SfxVolume;
        public float MusicVolume;
        public float AmbienceVolume;
        public float VoiceVolume;
        public float UiVolume;
        public float UiScale;
        public int GraphicsQuality;
        public float RenderScale;
        public int VSyncCount;
        public int AntiAliasing;
    }
}
