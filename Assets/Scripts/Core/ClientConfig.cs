#nullable enable

using System;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Core
{
    [Serializable]
    public class ClientConfig
    {
        public const int CurrentSchemaVersion = 15;

        public int SchemaVersion;
        public string ProjectDefaultsHash = string.Empty;
        public float MasterVolume;
        public float SfxVolume;
        public float MusicVolume;
        public float AmbienceVolume;
        public float VoiceVolume;
        public float UiVolume;
        public float UiScale;
        public string Language = "ru";
        public int ResolutionWidth;
        public int ResolutionHeight;
        public int RefreshRate;
        public int FullScreenMode = 1; // FullScreenWindow
        public bool VSync = true;
        public int TargetFrameRate = -1;
        public bool MuteAudioInBackground = true;
        [FormerlySerializedAs("GraphicsQuality")]

        public GraphicsPreset GraphicsPreset;
        public GraphicsQualitySettings GraphicsQualitySettings;
        public bool AmbientOcclusionEnabled;
        public bool DiffuseBounceEnabled;
        public float AmbientIntensity;
        public float EmissionScale;
        public Color AmbientColor;
        public Color EmptyExtinctionRgb;
        public Color SolidExtinctionRgb;
        public float EmptyExtinctionMultiplier;
        public float SolidExtinctionMultiplier;
        public float BounceStrength;
        public float AmbientOcclusionRadiusCells;
        public float AmbientOcclusionStrength;
        public float MaximumLightMultiplier;
        public bool EnableFinalLightingClamp;
        public float TransmittanceDebugDistanceCells;
        public float MinimumTransmission;
        public int LightSafeBorder;
        public float DynamicLightIntensity;
        public Color DynamicLightColor;
        public float DynamicLightUpdatesPerSecond;
        public Vector2 TerrainFlowScale;
        public float TerrainShimmerSpeedScale;
        public float TerrainPulseSpeedScale;
        public Color TerrainShimmerColor;
        public Color TerrainDebugColor;
        public bool TerrainDebugMode;
        public float BloomThreshold;
        public float BloomSoftKnee;
        public float BloomRadius;
        public float BloomScatter;
        public Color BloomTint;
        public Color TransitEmissionColor;
        public float TransitEmissionStrength;
        public Color PerspectiveEmissionColor;
        public float PerspectiveEmissionStrength;
        public float SurfaceOccupancy;
        public float BloomIntensity;
        public float VignetteIntensity;
        public Color VignetteColor;
        public float VignetteSmoothness;
        public Vector2 VignetteCenter;
        public float ChromaticAberrationIntensity;
        public float ColorGradingExposure;
        public Color ColorGradingFilter;
        public float ColorGradingContrast;
        public float ColorGradingSaturation;
        public bool ColorGradingToneMapping;
        public float ColorGradingToneMappingWhitePoint;
        public float EigengrauIntensity;
        public Color EigengrauColor;
        public float EigengrauDarknessThreshold;
        public float EigengrauNoiseScale;
        public float EigengrauAnimationSpeed;
        public float MotionBlurIntensity;
        public AdvancedPostProcessSettings AdvancedPostProcess = new();

        // Сетевое подключение. DummyConnection — заглушка для локального теста без
        // сервера. При UseDummyConnection = false клиент подключается к реальному
        // серверу через Darkar25 TcpConnection (MinesServerNetworking).
        public bool UseDummyConnection = true;
        public string ServerHost = "127.0.0.1";
        public int ServerPort = 7777;
    }
}
