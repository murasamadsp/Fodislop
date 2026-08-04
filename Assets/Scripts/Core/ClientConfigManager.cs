#nullable enable

using System.IO;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский локальный конфиг: survives перезапусков, живёт в Application.persistentDataPath.
    /// Никаких дефолтов из кода не используются как fallback — значения должны быть явно записаны
    /// через ApplyValues() или загружены из файла.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public class ClientConfigManager : MonoBehaviour, IClientConfigManager
    {
        public static ClientConfigManager? Instance { get; private set; }

        public ClientConfig Config { get; private set; } = new();

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
                Debug.Log($"[ClientConfigManager] Config loaded from {configPath}");
            }
            catch (System.Exception ex)
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
                Debug.Log($"[ClientConfigManager] Config saved to {configPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ClientConfigManager] Failed to save config: {ex.Message}");
            }
        }

        public void ApplyDefaults()
        {
            Config = new ClientConfig();
            Debug.Log("[ClientConfigManager] Applied default config values.");
        }
    }
}
