#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский локальный конфиг: survives перезапусков, живёт в Application.persistentDataPath.
    /// Дефолты — только из ClientConfig.Defaults. При Load() выполняется health check:
    /// битые/отрицательные/выходящие за диапазон значения исправляются, неизвестные поля игнорируются.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public class ClientConfigManager : MonoBehaviour, IClientConfigManager
    {
        public static ClientConfigManager? Instance { get; private set; }

        public ClientConfig Config { get; private set; } = ClientConfig.Defaults;

        private const string ConfigFileName = "client_config.json";
        private const string ConfigDirectory = "Config";

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        protected void Awake()
        {
            Instance = this;
            Load();
        }

        public void Load()
        {
            string configPath = GetConfigPath();
            if (!File.Exists(configPath))
            {
                ApplyDefaults();
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var loaded = JsonUtility.FromJson<ClientConfig>(json);
                if (loaded == null)
                {
                    Debug.LogWarning("[ClientConfigManager] Failed to parse config, recreating defaults.");
                    ApplyDefaults();
                    Save();
                    return;
                }

                Config = loaded;
                Validate();
                Debug.Log($"[ClientConfigManager] Config loaded and validated from {configPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClientConfigManager] Failed to load config: {ex.Message}. Recreating defaults.");
                ApplyDefaults();
                Save();
            }
        }

        public void Save()
        {
            try
            {
                string configPath = GetConfigPath();
                string directory = Path.GetDirectoryName(configPath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(Config, prettyPrint: true);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClientConfigManager] Failed to save config: {ex.Message}");
            }
        }

        public void ApplyDefaults()
        {
            Config = new ClientConfig
            {
                MasterVolume = ClientConfig.Defaults.MasterVolume,
                SfxVolume = ClientConfig.Defaults.SfxVolume,
                MusicVolume = ClientConfig.Defaults.MusicVolume,
                AmbienceVolume = ClientConfig.Defaults.AmbienceVolume,
                VoiceVolume = ClientConfig.Defaults.VoiceVolume,
                UiVolume = ClientConfig.Defaults.UiVolume,
                UiScale = ClientConfig.Defaults.UiScale,
                UseLight2D = ClientConfig.Defaults.UseLight2D,
                GraphicsQuality = ClientConfig.Defaults.GraphicsQuality,
                RenderScale = ClientConfig.Defaults.RenderScale,
                VSyncCount = ClientConfig.Defaults.VSyncCount,
                AntiAliasing = ClientConfig.Defaults.AntiAliasing,
            };
            Debug.Log("[ClientConfigManager] Applied default config values.");
        }

        /// <summary>
        /// Health check: исправляет битые значения, диапазоны, типы.
        /// JsonUtility сам отбрасывает неизвестные поля.
        /// </summary>
        private void Validate()
        {
            bool changed = false;

            if (FixFloat(Config.MasterVolume, 0f, 1f, ClientConfig.Defaults.MasterVolume))
            {
                Config.MasterVolume = ClientConfig.Defaults.MasterVolume;
                changed = true;
            }

            if (FixFloat(Config.SfxVolume, 0f, 1f, ClientConfig.Defaults.SfxVolume))
            {
                Config.SfxVolume = ClientConfig.Defaults.SfxVolume;
                changed = true;
            }

            if (FixFloat(Config.MusicVolume, 0f, 1f, ClientConfig.Defaults.MusicVolume))
            {
                Config.MusicVolume = ClientConfig.Defaults.MusicVolume;
                changed = true;
            }

            if (FixFloat(Config.AmbienceVolume, 0f, 1f, ClientConfig.Defaults.AmbienceVolume))
            {
                Config.AmbienceVolume = ClientConfig.Defaults.AmbienceVolume;
                changed = true;
            }

            if (FixFloat(Config.VoiceVolume, 0f, 1f, ClientConfig.Defaults.VoiceVolume))
            {
                Config.VoiceVolume = ClientConfig.Defaults.VoiceVolume;
                changed = true;
            }

            if (FixFloat(Config.UiVolume, 0f, 1f, ClientConfig.Defaults.UiVolume))
            {
                Config.UiVolume = ClientConfig.Defaults.UiVolume;
                changed = true;
            }

            if (FixFloat(Config.UiScale, 0.5f, 2f, ClientConfig.Defaults.UiScale))
            {
                Config.UiScale = ClientConfig.Defaults.UiScale;
                changed = true;
            }

            if (FixFloat(Config.RenderScale, 0.1f, 4f, ClientConfig.Defaults.RenderScale))
            {
                Config.RenderScale = ClientConfig.Defaults.RenderScale;
                changed = true;
            }

            if (Config.GraphicsQuality is < 0 or > 3)
            {
                Config.GraphicsQuality = ClientConfig.Defaults.GraphicsQuality;
                changed = true;
            }

            if (Config.VSyncCount is < 0 or > 4)
            {
                Config.VSyncCount = ClientConfig.Defaults.VSyncCount;
                changed = true;
            }

            if (Config.AntiAliasing is < 0 or > 8)
            {
                Config.AntiAliasing = ClientConfig.Defaults.AntiAliasing;
                changed = true;
            }

            if (changed)
            {
                Debug.LogWarning("[ClientConfigManager] Invalid config values were corrected. Saving sanitized config.");
                Save();
            }
        }

        private static bool FixFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            {
                return true;
            }

            return false;
        }
    }
}
