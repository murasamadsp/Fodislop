#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский локальный конфиг: survives перезапусков, живёт в Application.persistentDataPath.
    /// Initial values приходят только из injected ProjectDefaults. Повреждённый
    /// persisted config не исправляется тихо и останавливает startup.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public class ClientConfigManager : MonoBehaviour, IClientConfigManager
    {
        private const string ConfigFileName = "client_config.json";
        private const string ConfigDirectory = "Config";

        public static ClientConfigManager? Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Instance = null;
        }

        public ClientConfig Config { get; private set; } = null!;
        private bool _initialized;

        [Inject]
        private IProjectDefaults _projectDefaults = null!;

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || !ServiceLocator.IsInitialized)
            {
                return;
            }

            if (_projectDefaults == null)
            {
                throw new InvalidOperationException(
                    "[ClientConfigManager] ProjectDefaults must be injected before loading client config.");
            }

            Load();
            _initialized = true;
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

            string json;
            try
            {
                json = File.ReadAllText(configPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Failed to read client config '{configPath}'.",
                    ex);
            }

            ClientConfig loaded = JsonUtility.FromJson<ClientConfig>(json) ??
                throw new InvalidDataException($"Client config '{configPath}' is empty or invalid.");
            bool migrated = Migrate(loaded);
            Validate(loaded);
            Config = loaded;
            if (migrated)
            {
                Save();
            }

            Debug.Log(
                $"[ClientConfigManager] Config loaded and validated from {configPath}; " +
                $"GraphicsQuality={Config.GraphicsQuality}; rendering pipeline is always enabled");
        }

        public void Save()
        {
            string configPath = GetConfigPath();
            string directory = Path.GetDirectoryName(configPath) ??
                throw new InvalidOperationException("Client config path has no parent directory.");
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(Config, prettyPrint: true);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to save client config '{configPath}'.", ex);
            }
        }

        public void ApplyDefaults()
        {
            ClientDefaultsSnapshot defaults = _projectDefaults.Client;
            Config = new ClientConfig
            {
                SchemaVersion = ClientConfig.CurrentSchemaVersion,
                MasterVolume = defaults.MasterVolume,
                SfxVolume = defaults.SfxVolume,
                MusicVolume = defaults.MusicVolume,
                AmbienceVolume = defaults.AmbienceVolume,
                VoiceVolume = defaults.VoiceVolume,
                UiVolume = defaults.UiVolume,
                UiScale = defaults.UiScale,
                GraphicsQuality = defaults.GraphicsQuality,
                RenderScale = defaults.RenderScale,
                VSyncCount = defaults.VSyncCount,
                AntiAliasing = defaults.AntiAliasing,
            };
            Debug.Log("[ClientConfigManager] Applied default config values.");
        }

        /// <summary>
        /// Проверяет persisted данные без неявной подстановки defaults.
        /// </summary>
        private static void Validate(ClientConfig config)
        {
            if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported client config schema {config.SchemaVersion}; " +
                    $"expected {ClientConfig.CurrentSchemaVersion}.");
            }

            ValidateFloat(config.MasterVolume, 0f, 1f, nameof(config.MasterVolume));
            ValidateFloat(config.SfxVolume, 0f, 1f, nameof(config.SfxVolume));
            ValidateFloat(config.MusicVolume, 0f, 1f, nameof(config.MusicVolume));
            ValidateFloat(config.AmbienceVolume, 0f, 1f, nameof(config.AmbienceVolume));
            ValidateFloat(config.VoiceVolume, 0f, 1f, nameof(config.VoiceVolume));
            ValidateFloat(config.UiVolume, 0f, 1f, nameof(config.UiVolume));
            ValidateFloat(config.UiScale, 0.5f, 2f, nameof(config.UiScale));
            ValidateFloat(config.RenderScale, 0.1f, 4f, nameof(config.RenderScale));
            ValidateInt(config.GraphicsQuality, 0, 3, nameof(config.GraphicsQuality));
            ValidateInt(config.VSyncCount, 0, 4, nameof(config.VSyncCount));
            ValidateInt(config.AntiAliasing, 0, 8, nameof(config.AntiAliasing));
        }

        private static bool Migrate(ClientConfig config)
        {
            if (config.SchemaVersion == 0)
            {
                config.SchemaVersion = ClientConfig.CurrentSchemaVersion;
                return true;
            }

            return false;
        }

        private static void ValidateFloat(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new InvalidDataException(
                    $"Client config value '{name}' must be finite and within [{minimum}, {maximum}].");
            }
        }

        private static void ValidateInt(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidDataException(
                    $"Client config value '{name}' must be within [{minimum}, {maximum}].");
            }
        }
    }
}
