#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting;
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

        public ClientConfig Config { get; private set; } = null!;
        public string ConfigFilePath => GetConfigPath();
        public GraphicsPreset SelectedGraphicsPreset => Config.GraphicsPreset;
        private bool _initialized;

        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        [Inject]
        private GraphicsQualityProfile _graphicsQualityProfile = null!;

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        private void Awake()
        {
        }

        private void Start()
        {
            if (DependenciesReady)
            {
                TryInitialize();
            }
        }

        private void Update()
        {
            if (!_initialized && DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool DependenciesReady =>
            _projectDefaults != null &&
            _graphicsQualityProfile != null;

        private void TryInitialize()
        {
            if (_initialized)
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

        /// <summary>
        /// Forces config load synchronously, without waiting for the next
        /// Start/Update cycle. This manager is an authored Bootstrap-tier
        /// singleton authored under BootstrapLifetimeScope:
        /// its Start() runs a frame later — too late for GameBootstrap.PostStart,
        /// which reads Config in the same frame the manager is created.
        /// EnsureInitialized is called at Bootstrap startup (BootstrapLifetimeScope.Awake)
        /// before any game scope is built.
        /// </summary>
        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            TryInitialize();
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

            json = RenameLegacyKeys(json);

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
                $"GraphicsPreset={Config.GraphicsPreset}; rendering pipeline is always enabled");
        }

        public void ApplyDefaults()
        {
            ClientDefaultsSnapshot defaults = _projectDefaults.Client;
            LightingDefaultsSnapshot lighting = _projectDefaults.Lighting;
            ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
            GraphicsPreset graphicsPreset = ConvertLegacyGraphicsQuality(
                defaults.GraphicsQuality);
            Config = new ClientConfig
            {
                SchemaVersion = ClientConfig.CurrentSchemaVersion,
                ProjectDefaultsHash = _projectDefaults.ContentHash,
                MasterVolume = defaults.MasterVolume,
                SfxVolume = defaults.SfxVolume,
                MusicVolume = defaults.MusicVolume,
                AmbienceVolume = defaults.AmbienceVolume,
                VoiceVolume = defaults.VoiceVolume,
                UIVolume = defaults.UIVolume,
                UIScale = defaults.UIScale,
                GraphicsPreset = graphicsPreset,
                GraphicsQualitySettings = _graphicsQualityProfile.Get(graphicsPreset),
                AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled,
                DiffuseBounceEnabled = lighting.DiffuseBounceEnabled,
                AmbientIntensity = lighting.AmbientIntensity,
                EmissionScale = lighting.EmissionScale,
                AmbientColor = lighting.AmbientColor,
                EmptyExtinctionRgb = lighting.EmptyExtinctionRgb,
                SolidExtinctionRgb = lighting.SolidExtinctionRgb,
                EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier,
                SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier,
                BounceStrength = lighting.BounceStrength,
                AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells,
                AmbientOcclusionStrength = lighting.AmbientOcclusionStrength,
                MaximumLightMultiplier = lighting.MaximumLightMultiplier,
                EnableFinalLightingClamp = lighting.EnableFinalLightingClamp,
                TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells,
                MinimumTransmission = lighting.MinimumTransmission,
                LightSafeBorder = lighting.LightSafeBorder,
                DynamicLightIntensity = lighting.DynamicLightIntensity,
                DynamicLightColor = lighting.DynamicLightColor,
                DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond,
                TerrainFlowScale = shaders.TerrainFlowScale,
                TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale,
                TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale,
                TerrainShimmerColor = shaders.TerrainShimmerColor,
                TerrainDebugColor = shaders.TerrainDebugColor,
                TerrainDebugMode = shaders.TerrainDebugMode,
                BloomThreshold = shaders.BloomThreshold,
                BloomSoftKnee = shaders.BloomSoftKnee,
                BloomRadius = shaders.BloomRadius,
                BloomScatter = shaders.BloomScatter,
                BloomTint = shaders.BloomTint,
                TransitEmissionColor = shaders.TransitEmissionColor,
                TransitEmissionStrength = shaders.TransitEmissionStrength,
                PerspectiveEmissionColor = shaders.PerspectiveEmissionColor,
                PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength,
                SurfaceOccupancy = shaders.SurfaceOccupancy,
                BloomIntensity = shaders.BloomIntensity,
                VignetteIntensity = shaders.VignetteIntensity,
                VignetteColor = shaders.VignetteColor,
                VignetteSmoothness = shaders.VignetteSmoothness,
                VignetteCenter = shaders.VignetteCenter,
                ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity,
                ColorGradingExposure = shaders.ColorGradingExposure,
                ColorGradingFilter = shaders.ColorGradingFilter,
                ColorGradingContrast = shaders.ColorGradingContrast,
                ColorGradingSaturation = shaders.ColorGradingSaturation,
                ColorGradingToneMapping = shaders.ColorGradingToneMapping,
                ColorGradingToneMappingWhitePoint = shaders.ColorGradingToneMappingWhitePoint,
                EigengrauIntensity = shaders.EigengrauIntensity,
                EigengrauColor = shaders.EigengrauColor,
                EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold,
                EigengrauNoiseScale = shaders.EigengrauNoiseScale,
                EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed,
                MotionBlurIntensity = shaders.MotionBlurIntensity,
                UseDummyConnection = true,
                ServerHost = "127.0.0.1",
                ServerPort = 7777,
            };
            Debug.Log("[ClientConfigManager] Applied explicit ProjectDefaults config values.");
        }

        public void MarkGraphicsAsCustom()
        {
            if (Config.GraphicsPreset == GraphicsPreset.Custom)
            {
                return;
            }

            if (!GraphicsQualityProfile.IsStandard(Config.GraphicsPreset))
            {
                throw new InvalidOperationException(
                    $"Cannot promote unknown graphics preset '{Config.GraphicsPreset}' to Custom.");
            }

            Config.GraphicsQualitySettings = _graphicsQualityProfile.Get(Config.GraphicsPreset);
            Config.GraphicsPreset = GraphicsPreset.Custom;
        }

        public void SelectGraphicsPreset(GraphicsPreset preset)
        {
            if (!GraphicsQualityProfile.IsStandard(preset))
            {
                throw new ArgumentException(
                    "Only one of the six immutable standard presets can be selected directly.",
                    nameof(preset));
            }

            Config.GraphicsPreset = preset;
            Config.GraphicsQualitySettings = _graphicsQualityProfile.Get(preset);
            ApplyLightingDefaults(Config, _projectDefaults.Lighting);
            ApplyShaderDefaults(Config, _projectDefaults.Shaders);
            Config.AdvancedPostProcess = new AdvancedPostProcessSettings();
        }

        public void SetCustomGraphicsSettings(GraphicsQualitySettings settings)
        {
            MarkGraphicsAsCustom();
            GraphicsQualityProfile.ValidateSettings(settings, "Custom");
            Config.GraphicsQualitySettings = settings;
        }

        public void UpdateAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            update(Config);
            Save();
        }

        public void UpdatePostProcessAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            MarkGraphicsAsCustom();
            update(Config);
            PromotePostProcessQualityForEnabledEffects(Config);
            Save();
        }

        private static void PromotePostProcessQualityForEnabledEffects(ClientConfig config)
        {
            AdvancedPostProcessSettings advanced = config.AdvancedPostProcess;
            bool requiresFull = config.BloomIntensity > 0f ||
                config.MotionBlurIntensity > 0f ||
                advanced.RequiresBloomTexture();
            bool requiresEssential = requiresFull ||
                config.VignetteIntensity > 0f ||
                config.ChromaticAberrationIntensity > 0f ||
                config.ColorGradingToneMapping ||
                Mathf.Abs(config.ColorGradingExposure) > 0.001f ||
                Mathf.Abs(config.ColorGradingContrast) > 0.001f ||
                Mathf.Abs(config.ColorGradingSaturation - 1f) > 0.001f ||
                config.EigengrauIntensity > 0f ||
                advanced.HasAnyEffects();

            GraphicsQualitySettings quality = config.GraphicsQualitySettings;
            if (requiresFull)
            {
                quality.PostProcessQuality = PostProcessQualityMode.Full;
            }
            else if (requiresEssential && quality.PostProcessQuality == PostProcessQualityMode.Off)
            {
                quality.PostProcessQuality = PostProcessQualityMode.Essential;
            }

            config.GraphicsQualitySettings = quality;
        }

        public void Save()
        {
            Validate(Config);
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

        /// <summary>
        /// Проверяет persisted данные без неявной подстановки defaults.
        /// </summary>
        private void Validate(ClientConfig config)
        {
            if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported client config schema {config.SchemaVersion}; " +
                    $"expected {ClientConfig.CurrentSchemaVersion}.");
            }

            if (!string.Equals(
                    config.ProjectDefaultsHash,
                    _projectDefaults.ContentHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Client config ProjectDefaultsHash does not match the active ProjectDefaults snapshot.");
            }

            ValidateFloat(config.MasterVolume, 0f, 1f, nameof(config.MasterVolume));
            ValidateFloat(config.SfxVolume, 0f, 1f, nameof(config.SfxVolume));
            ValidateFloat(config.MusicVolume, 0f, 1f, nameof(config.MusicVolume));
            ValidateFloat(config.AmbienceVolume, 0f, 1f, nameof(config.AmbienceVolume));
            ValidateFloat(config.VoiceVolume, 0f, 1f, nameof(config.VoiceVolume));
            ValidateFloat(config.UIVolume, 0f, 1f, nameof(config.UIVolume));
            ValidateFloat(config.UIScale, 0.5f, 2f, nameof(config.UIScale));
            if (!Enum.IsDefined(typeof(GraphicsPreset), config.GraphicsPreset))
            {
                throw new InvalidDataException(
                    $"Unknown graphics preset value '{config.GraphicsPreset}'.");
            }

            try
            {
                GraphicsQualityProfile.ValidateSettings(
                    config.GraphicsQualitySettings,
                    config.GraphicsPreset.ToString());
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException(
                    "Client graphics quality settings are invalid.",
                    ex);
            }

            if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
                config.GraphicsQualitySettings != _graphicsQualityProfile.Get(config.GraphicsPreset))
            {
                throw new InvalidDataException(
                    $"Standard graphics preset '{config.GraphicsPreset}' was mutated in client config.");
            }

            if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
                !HasStandardGraphicsValues(config))
            {
                throw new InvalidDataException(
                    $"Standard graphics preset '{config.GraphicsPreset}' contains customized visual values. " +
                    "Mark the preset as Custom before changing graphics settings.");
            }

            ValidateFloat(config.AmbientIntensity, 0f, 1f, nameof(config.AmbientIntensity));
            ValidateFloat(config.EmissionScale, 0.1f, 8f, nameof(config.EmissionScale));
            ValidateColor(config.AmbientColor, nameof(config.AmbientColor));
            ValidateColor(config.EmptyExtinctionRgb, nameof(config.EmptyExtinctionRgb));
            ValidateColor(config.SolidExtinctionRgb, nameof(config.SolidExtinctionRgb));
            ValidateFloat(config.EmptyExtinctionMultiplier, 0f, 2f, nameof(config.EmptyExtinctionMultiplier));
            ValidateFloat(config.SolidExtinctionMultiplier, 0.25f, 2f, nameof(config.SolidExtinctionMultiplier));
            ValidateFloat(config.BounceStrength, 0f, 1f, nameof(config.BounceStrength));
            ValidateFloat(config.AmbientOcclusionRadiusCells, 0.5f, 8f, nameof(config.AmbientOcclusionRadiusCells));
            ValidateFloat(config.AmbientOcclusionStrength, 0.1f, 8f, nameof(config.AmbientOcclusionStrength));
            ValidateFloat(config.MaximumLightMultiplier, 0.25f, LightingConfigLimits.MaximumLightMultiplier, nameof(config.MaximumLightMultiplier));
            ValidateFloat(config.TransmittanceDebugDistanceCells, 2f, 32f, nameof(config.TransmittanceDebugDistanceCells));
            ValidateFloat(config.MinimumTransmission, 0.0001f, 0.1f, nameof(config.MinimumTransmission));
            ValidateInt(config.LightSafeBorder, 0, 8, nameof(config.LightSafeBorder));
            ValidateFloat(config.DynamicLightIntensity, 0f, 4f, nameof(config.DynamicLightIntensity));
            ValidateColor(config.DynamicLightColor, nameof(config.DynamicLightColor));
            ValidateFloat(config.DynamicLightUpdatesPerSecond, 1f, LightingConfigLimits.DynamicLightUpdatesPerSecond, nameof(config.DynamicLightUpdatesPerSecond));
            ValidateFloat(config.TerrainFlowScale.x, 0.001f, 1024f, nameof(config.TerrainFlowScale.x));
            ValidateFloat(config.TerrainFlowScale.y, 0.001f, 1024f, nameof(config.TerrainFlowScale.y));
            ValidateFloat(config.TerrainShimmerSpeedScale, 0f, 10f, nameof(config.TerrainShimmerSpeedScale));
            ValidateFloat(config.TerrainPulseSpeedScale, 0f, 10f, nameof(config.TerrainPulseSpeedScale));
            ValidateColor(config.TerrainShimmerColor, nameof(config.TerrainShimmerColor));
            ValidateColor(config.TerrainDebugColor, nameof(config.TerrainDebugColor));
            ValidateFloat(config.BloomThreshold, 0f, 2f, nameof(config.BloomThreshold));
            ValidateFloat(config.BloomSoftKnee, 0f, 1f, nameof(config.BloomSoftKnee));
            ValidateFloat(config.BloomRadius, 0.5f, 8f, nameof(config.BloomRadius));
            ValidateFloat(config.BloomScatter, 0.1f, 1f, nameof(config.BloomScatter));
            ValidateColor(config.BloomTint, nameof(config.BloomTint));
            ValidateColor(config.TransitEmissionColor, nameof(config.TransitEmissionColor));
            ValidateFloat(config.TransitEmissionStrength, 0f, 8f, nameof(config.TransitEmissionStrength));
            ValidateColor(config.PerspectiveEmissionColor, nameof(config.PerspectiveEmissionColor));
            ValidateFloat(config.PerspectiveEmissionStrength, 0f, 8f, nameof(config.PerspectiveEmissionStrength));
            ValidateFloat(config.SurfaceOccupancy, 0f, 1f, nameof(config.SurfaceOccupancy));
            ValidateFloat(config.BloomIntensity, 0f, 5f, nameof(config.BloomIntensity));
            ValidateFloat(config.VignetteIntensity, 0f, 1f, nameof(config.VignetteIntensity));
            ValidateColor(config.VignetteColor, nameof(config.VignetteColor));
            ValidateFloat(config.VignetteSmoothness, 0.01f, 1f, nameof(config.VignetteSmoothness));
            ValidateFloat(config.VignetteCenter.x, 0f, 1f, nameof(config.VignetteCenter.x));
            ValidateFloat(config.VignetteCenter.y, 0f, 1f, nameof(config.VignetteCenter.y));
            ValidateFloat(config.ChromaticAberrationIntensity, 0f, 1f, nameof(config.ChromaticAberrationIntensity));
            ValidateFloat(config.ColorGradingExposure, -4f, 4f, nameof(config.ColorGradingExposure));
            ValidateColor(config.ColorGradingFilter, nameof(config.ColorGradingFilter));
            ValidateFloat(config.ColorGradingSaturation, 0f, 2f, nameof(config.ColorGradingSaturation));
            ValidateFloat(
                config.ColorGradingToneMappingWhitePoint,
                0.25f,
                8f,
                nameof(config.ColorGradingToneMappingWhitePoint));
            ValidateFloat(config.EigengrauIntensity, 0f, 1f, nameof(config.EigengrauIntensity));
            ValidateColor(config.EigengrauColor, nameof(config.EigengrauColor));
            ValidateFloat(config.EigengrauDarknessThreshold, 0.02f, 0.75f, nameof(config.EigengrauDarknessThreshold));
            ValidateFloat(config.EigengrauNoiseScale, 0.75f, 2f, nameof(config.EigengrauNoiseScale));
            ValidateFloat(config.EigengrauAnimationSpeed, 1f, 60f, nameof(config.EigengrauAnimationSpeed));
            ValidateFloat(config.MotionBlurIntensity, 0f, 1f, nameof(config.MotionBlurIntensity));
            AdvancedPostProcessSettings advanced = config.AdvancedPostProcess ??
                throw new InvalidDataException("AdvancedPostProcess settings are missing.");
            ValidateFloat(advanced.LocalContrastIntensity, 0f, 0.5f, nameof(advanced.LocalContrastIntensity));
            ValidateFloat(advanced.LensDirtIntensity, 0f, 0.35f, nameof(advanced.LensDirtIntensity));
            ValidateFloat(advanced.LensDirtScale, 0.25f, 16f, nameof(advanced.LensDirtScale));
            ValidateFloat(advanced.AnamorphicIntensity, 0f, 1f, nameof(advanced.AnamorphicIntensity));
            ValidateFloat(advanced.AnamorphicLength, 0.25f, 8f, nameof(advanced.AnamorphicLength));
            ValidateFloat(advanced.ChromaticDiffractionIntensity, 0f, 0.5f, nameof(advanced.ChromaticDiffractionIntensity));
            ValidateFloat(advanced.HeatRefractionIntensity, 0f, 0.25f, nameof(advanced.HeatRefractionIntensity));
            ValidateFloat(advanced.HeatRefractionScale, 0.25f, 16f, nameof(advanced.HeatRefractionScale));
            ValidateFloat(advanced.GlintIntensity, 0f, 0.5f, nameof(advanced.GlintIntensity));
            ValidateFloat(advanced.GlintThreshold, 0f, 4f, nameof(advanced.GlintThreshold));
            ValidateFloat(advanced.VolumetricDustIntensity, 0f, 0.25f, nameof(advanced.VolumetricDustIntensity));
            ValidateFloat(advanced.VolumetricDustScale, 0.1f, 8f, nameof(advanced.VolumetricDustScale));
            ValidateFloat(advanced.VolumetricDustSpeed, 0f, 2f, nameof(advanced.VolumetricDustSpeed));
            ValidateFloat(advanced.PhosphorMaskIntensity, 0f, 0.35f, nameof(advanced.PhosphorMaskIntensity));
            ValidateFloat(advanced.DitheringIntensity, 0f, 1f, nameof(advanced.DitheringIntensity));
            ValidateFloat(advanced.TemporalPersistenceIntensity, 0f, 0.8f, nameof(advanced.TemporalPersistenceIntensity));
            ValidateFloat(advanced.TemporalPersistenceDecay, 0f, 0.98f, nameof(advanced.TemporalPersistenceDecay));
            ValidateFloat(advanced.LightStability, 0f, 0.9f, nameof(advanced.LightStability));
            if (string.IsNullOrWhiteSpace(config.ServerHost))
            {
                throw new InvalidDataException(
                    "Client config value 'ServerHost' must be a non-empty host name or IP address.");
            }

            ValidateInt(config.ServerPort, 1, 65535, nameof(config.ServerPort));
            if (!Enum.IsDefined(typeof(FullScreenMode), config.FullScreenMode))
            {
                throw new InvalidDataException(
                    $"Client config value 'FullScreenMode' must be a valid FullScreenMode value, got {config.FullScreenMode}.");
            }
        }

        /// <summary>
        /// Переименовывает ключи, оставшиеся от старых версий конфига.
        ///
        /// JsonUtility сопоставляет JSON с полями ПО ИМЕНИ и молча подставляет
        /// значение по умолчанию для всего, чего не нашёл. Поэтому переименование
        /// поля `UiScale` -> `UIScale` без этого шага не выдало бы ошибки — оно
        /// просто сбросило бы пользователю масштаб интерфейса и громкость UI при
        /// первом же запуске. Правка идёт по сырому тексту, до десериализации,
        /// потому что после неё старого ключа уже не существует.
        /// </summary>
        private static string RenameLegacyKeys(string json)
        {
            return json
                .Replace("\"UiScale\"", "\"UIScale\"")
                .Replace("\"UiVolume\"", "\"UIVolume\"");
        }

        private bool Migrate(ClientConfig config)
        {
            ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
            bool migrated = false;
            if (config.SchemaVersion < 2)
            {
                config.TerrainFlowScale = shaders.TerrainFlowScale;
                config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
                config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
                config.TerrainShimmerColor = shaders.TerrainShimmerColor;
                config.TerrainDebugColor = shaders.TerrainDebugColor;
                config.TerrainDebugMode = shaders.TerrainDebugMode;
                config.TransitEmissionColor = shaders.TransitEmissionColor;
                config.TransitEmissionStrength = shaders.TransitEmissionStrength;
                config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
                config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
                config.SurfaceOccupancy = shaders.SurfaceOccupancy;
                config.BloomIntensity = shaders.BloomIntensity;
                config.VignetteIntensity = shaders.VignetteIntensity;
                config.ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity;
                config.ColorGradingExposure = shaders.ColorGradingExposure;
                config.ColorGradingContrast = shaders.ColorGradingContrast;
                config.ColorGradingSaturation = shaders.ColorGradingSaturation;
                config.ColorGradingToneMapping = shaders.ColorGradingToneMapping;
                config.EigengrauIntensity = shaders.EigengrauIntensity;
                config.MotionBlurIntensity = shaders.MotionBlurIntensity;
                config.SchemaVersion = 2;
                migrated = true;
            }

            if (config.SchemaVersion < 3)
            {
                config.BloomThreshold = shaders.BloomThreshold;
                config.BloomScatter = shaders.BloomScatter;
                config.BloomTint = shaders.BloomTint;
                config.VignetteColor = shaders.VignetteColor;
                config.VignetteSmoothness = shaders.VignetteSmoothness;
                config.VignetteCenter = shaders.VignetteCenter;
                config.ColorGradingFilter = shaders.ColorGradingFilter;
                config.ColorGradingToneMappingWhitePoint =
                    shaders.ColorGradingToneMappingWhitePoint;
                config.EigengrauColor = shaders.EigengrauColor;
                config.EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold;
                config.EigengrauNoiseScale = shaders.EigengrauNoiseScale;
                config.EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed;
                config.SchemaVersion = 3;
                migrated = true;
            }

            if (config.SchemaVersion < 4)
            {
                config.TerrainDebugColor = shaders.TerrainDebugColor;
                config.TerrainDebugMode = shaders.TerrainDebugMode;
                config.SchemaVersion = 4;
                migrated = true;
            }

            if (config.SchemaVersion < 5)
            {
                // World-object shader settings were removed with the unused
                // legacy shader. JsonUtility will omit those obsolete fields
                // when this migrated config is persisted.
                config.SchemaVersion = 5;
                migrated = true;
            }

            if (config.SchemaVersion < 6)
            {
                ApplyShaderDefaults(config, shaders);
                config.ProjectDefaultsHash = _projectDefaults.ContentHash;
                config.SchemaVersion = 6;
                migrated = true;
            }

            if (config.SchemaVersion < 7)
            {
                config.ProjectDefaultsHash = _projectDefaults.ContentHash;
                config.SchemaVersion = 7;
                migrated = true;
            }

            if (config.SchemaVersion < 8)
            {
                ApplyLightingDefaults(config, _projectDefaults.Lighting);
                config.SchemaVersion = 8;
                migrated = true;
            }

            if (config.SchemaVersion < 9)
            {
                GraphicsPreset previousPreset = ConvertLegacyGraphicsQuality(
                    (int)config.GraphicsPreset);
                config.GraphicsQualitySettings = _graphicsQualityProfile.Get(previousPreset);

                // Before schema 9, manual settings did not change the quality
                // label. Preserve every persisted visual value by treating the
                // migrated configuration as Custom instead of overwriting it
                // with a standard preset.
                config.GraphicsPreset = GraphicsPreset.Custom;
                config.SchemaVersion = 9;
                migrated = true;
            }

            if (config.SchemaVersion < 10)
            {
                // Schema 10 added explicit network transport settings. The
                // offline stub stays the default so existing local setups keep
                // working without a server; real networking is one flag away.
                config.UseDummyConnection = true;
                config.ServerHost = "127.0.0.1";
                config.ServerPort = 7777;
                config.SchemaVersion = 10;
                migrated = true;
            }

            if (config.SchemaVersion < 11)
            {
                // Schema 11 carried no new fields: the version was bumped to
                // force a re-save after the schema-10 display fields landed.
                // JsonUtility fills missing fields (Language, VSync,
                // MuteAudioInBackground, FullScreenMode, TargetFrameRate,
                // UseDummyConnection, ServerHost, ServerPort) from their C#
                // initializers, so an old persisted file already holds safe
                // defaults. Only the version marker needs updating.
                config.SchemaVersion = 11;
                migrated = true;
            }

            if (config.SchemaVersion < 12)
            {
                // The old Custom slider allowed 128 even though the maximum
                // zoom viewport plus the stable lighting border is wider than
                // that. Such a config validated successfully and then failed
                // deterministically before the first lighting solve. Raise the
                // persisted hard limit to the smallest supported field size;
                // this is a schema correction, not a runtime quality fallback.
                config.GraphicsQualitySettings.LightingMaximumTextureDimension =
                    Mathf.Max(
                        config.GraphicsQualitySettings.LightingMaximumTextureDimension,
                        GraphicsQualitySettings.MinimumLightingTextureDimension);
                config.SchemaVersion = 12;
                migrated = true;
            }

            if (config.SchemaVersion < 13)
            {
                config.BloomSoftKnee = shaders.BloomSoftKnee;
                config.BloomRadius = shaders.BloomRadius;
                config.SchemaVersion = 13;
                migrated = true;
            }

            if (config.SchemaVersion < 14)
            {
                config.AdvancedPostProcess = new AdvancedPostProcessSettings();
                config.SchemaVersion = 14;
                migrated = true;
            }

            if (config.SchemaVersion < 15)
            {
                AdvancedPostProcessSettings advanced = config.AdvancedPostProcess;
                config.BloomIntensity = Mathf.Clamp(config.BloomIntensity, 0f, 2f);
                config.BloomTint = new Color(
                    Mathf.Clamp(config.BloomTint.r, 0f, 2f),
                    Mathf.Clamp(config.BloomTint.g, 0f, 2f),
                    Mathf.Clamp(config.BloomTint.b, 0f, 2f),
                    Mathf.Clamp01(config.BloomTint.a));
                config.ChromaticAberrationIntensity = Mathf.Clamp(
                    config.ChromaticAberrationIntensity,
                    0f,
                    0.25f);
                config.ColorGradingExposure = Mathf.Clamp(
                    config.ColorGradingExposure,
                    -2f,
                    2f);
                config.ColorGradingContrast = Mathf.Clamp(
                    config.ColorGradingContrast,
                    -0.5f,
                    0.5f);
                config.EigengrauIntensity = Mathf.Clamp(config.EigengrauIntensity, 0f, 0.25f);
                config.MotionBlurIntensity = Mathf.Clamp(config.MotionBlurIntensity, 0f, 0.5f);
                advanced.LocalContrastIntensity = Mathf.Clamp(advanced.LocalContrastIntensity, 0f, 0.5f);
                advanced.LensDirtIntensity = Mathf.Clamp(advanced.LensDirtIntensity, 0f, 0.35f);
                advanced.AnamorphicIntensity = Mathf.Clamp01(advanced.AnamorphicIntensity);
                advanced.ChromaticDiffractionIntensity = Mathf.Clamp(advanced.ChromaticDiffractionIntensity, 0f, 0.5f);
                advanced.HeatRefractionIntensity = Mathf.Clamp(advanced.HeatRefractionIntensity, 0f, 0.25f);
                advanced.GlintIntensity = Mathf.Clamp(advanced.GlintIntensity, 0f, 0.5f);
                advanced.VolumetricDustIntensity = Mathf.Clamp(advanced.VolumetricDustIntensity, 0f, 0.25f);
                advanced.PhosphorMaskIntensity = Mathf.Clamp(advanced.PhosphorMaskIntensity, 0f, 0.35f);
                advanced.TemporalPersistenceIntensity = Mathf.Clamp(advanced.TemporalPersistenceIntensity, 0f, 0.8f);
                advanced.TemporalPersistenceDecay = Mathf.Clamp(advanced.TemporalPersistenceDecay, 0f, 0.98f);
                advanced.LightStability = Mathf.Clamp(advanced.LightStability, 0f, 0.9f);
                config.SchemaVersion = 15;
                migrated = true;
            }

            if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
            {
                GraphicsQualitySettings standardSettings =
                    _graphicsQualityProfile.Get(config.GraphicsPreset);
                if (config.GraphicsQualitySettings != standardSettings)
                {
                    config.GraphicsQualitySettings = standardSettings;
                    migrated = true;
                }
            }

            if (!string.Equals(
                    config.ProjectDefaultsHash,
                    _projectDefaults.ContentHash,
                    StringComparison.Ordinal))
            {
                if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
                {
                    ApplyLightingDefaults(config, _projectDefaults.Lighting);
                    ApplyShaderDefaults(config, _projectDefaults.Shaders);
                    config.AdvancedPostProcess = new AdvancedPostProcessSettings();
                    Debug.Log(
                        "[ClientConfigManager] ProjectDefaults changed; refreshed the selected " +
                        "immutable standard graphics preset.");
                }
                else
                {
                    Debug.Log(
                        "[ClientConfigManager] ProjectDefaults changed; preserved Custom graphics settings.");
                }

                config.ProjectDefaultsHash = _projectDefaults.ContentHash;
                migrated = true;
            }

            return migrated;
        }

        private static GraphicsPreset ConvertLegacyGraphicsQuality(int legacyQuality)
        {
            return legacyQuality switch
            {
                0 => GraphicsPreset.Low,
                1 => GraphicsPreset.Medium,
                2 => GraphicsPreset.High,
                3 => GraphicsPreset.Ultra,
                _ => throw new InvalidDataException(
                    $"Legacy graphics quality '{legacyQuality}' is outside the supported range 0..3."),
            };
        }

        private static void ApplyShaderDefaults(
            ClientConfig config,
            ShaderDefaultsSnapshot shaders)
        {
            config.TerrainFlowScale = shaders.TerrainFlowScale;
            config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
            config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
            config.TerrainShimmerColor = shaders.TerrainShimmerColor;
            config.TerrainDebugColor = shaders.TerrainDebugColor;
            config.TerrainDebugMode = shaders.TerrainDebugMode;
            config.BloomThreshold = shaders.BloomThreshold;
            config.BloomSoftKnee = shaders.BloomSoftKnee;
            config.BloomRadius = shaders.BloomRadius;
            config.BloomScatter = shaders.BloomScatter;
            config.BloomTint = shaders.BloomTint;
            config.TransitEmissionColor = shaders.TransitEmissionColor;
            config.TransitEmissionStrength = shaders.TransitEmissionStrength;
            config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
            config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
            config.SurfaceOccupancy = shaders.SurfaceOccupancy;
            config.BloomIntensity = shaders.BloomIntensity;
            config.VignetteIntensity = shaders.VignetteIntensity;
            config.VignetteColor = shaders.VignetteColor;
            config.VignetteSmoothness = shaders.VignetteSmoothness;
            config.VignetteCenter = shaders.VignetteCenter;
            config.ChromaticAberrationIntensity = shaders.ChromaticAberrationIntensity;
            config.ColorGradingExposure = shaders.ColorGradingExposure;
            config.ColorGradingFilter = shaders.ColorGradingFilter;
            config.ColorGradingContrast = shaders.ColorGradingContrast;
            config.ColorGradingSaturation = shaders.ColorGradingSaturation;
            config.ColorGradingToneMapping = shaders.ColorGradingToneMapping;
            config.ColorGradingToneMappingWhitePoint = shaders.ColorGradingToneMappingWhitePoint;
            config.EigengrauIntensity = shaders.EigengrauIntensity;
            config.EigengrauColor = shaders.EigengrauColor;
            config.EigengrauDarknessThreshold = shaders.EigengrauDarknessThreshold;
            config.EigengrauNoiseScale = shaders.EigengrauNoiseScale;
            config.EigengrauAnimationSpeed = shaders.EigengrauAnimationSpeed;
            config.MotionBlurIntensity = shaders.MotionBlurIntensity;
        }

        private static void ApplyLightingDefaults(
            ClientConfig config,
            LightingDefaultsSnapshot lighting)
        {
            config.AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = lighting.DiffuseBounceEnabled;
            config.AmbientIntensity = lighting.AmbientIntensity;
            config.EmissionScale = lighting.EmissionScale;
            config.AmbientColor = lighting.AmbientColor;
            config.EmptyExtinctionRgb = lighting.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = lighting.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier;
            config.BounceStrength = lighting.BounceStrength;
            config.AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = lighting.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = lighting.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = lighting.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = lighting.MinimumTransmission;
            config.LightSafeBorder = lighting.LightSafeBorder;
            config.DynamicLightIntensity = lighting.DynamicLightIntensity;
            config.DynamicLightColor = lighting.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond;
        }

        private bool HasStandardGraphicsValues(ClientConfig config)
        {
            LightingDefaultsSnapshot lighting = _projectDefaults.Lighting;
            ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
            return config.AmbientOcclusionEnabled == lighting.AmbientOcclusionEnabled &&
                config.DiffuseBounceEnabled == lighting.DiffuseBounceEnabled &&
                config.AmbientIntensity == lighting.AmbientIntensity &&
                config.EmissionScale == lighting.EmissionScale &&
                config.AmbientColor == lighting.AmbientColor &&
                config.EmptyExtinctionRgb == lighting.EmptyExtinctionRgb &&
                config.SolidExtinctionRgb == lighting.SolidExtinctionRgb &&
                config.EmptyExtinctionMultiplier == lighting.EmptyExtinctionMultiplier &&
                config.SolidExtinctionMultiplier == lighting.SolidExtinctionMultiplier &&
                config.BounceStrength == lighting.BounceStrength &&
                config.AmbientOcclusionRadiusCells == lighting.AmbientOcclusionRadiusCells &&
                config.AmbientOcclusionStrength == lighting.AmbientOcclusionStrength &&
                config.MaximumLightMultiplier == lighting.MaximumLightMultiplier &&
                config.EnableFinalLightingClamp == lighting.EnableFinalLightingClamp &&
                config.TransmittanceDebugDistanceCells == lighting.TransmittanceDebugDistanceCells &&
                config.MinimumTransmission == lighting.MinimumTransmission &&
                config.LightSafeBorder == lighting.LightSafeBorder &&
                config.DynamicLightIntensity == lighting.DynamicLightIntensity &&
                config.DynamicLightColor == lighting.DynamicLightColor &&
                config.DynamicLightUpdatesPerSecond == lighting.DynamicLightUpdatesPerSecond &&
                config.TerrainFlowScale == shaders.TerrainFlowScale &&
                config.TerrainShimmerSpeedScale == shaders.TerrainShimmerSpeedScale &&
                config.TerrainPulseSpeedScale == shaders.TerrainPulseSpeedScale &&
                config.TerrainShimmerColor == shaders.TerrainShimmerColor &&
                config.TerrainDebugColor == shaders.TerrainDebugColor &&
                config.TerrainDebugMode == shaders.TerrainDebugMode &&
                config.BloomThreshold == shaders.BloomThreshold &&
                config.BloomSoftKnee == shaders.BloomSoftKnee &&
                config.BloomRadius == shaders.BloomRadius &&
                config.BloomScatter == shaders.BloomScatter &&
                config.BloomTint == shaders.BloomTint &&
                config.TransitEmissionColor == shaders.TransitEmissionColor &&
                config.TransitEmissionStrength == shaders.TransitEmissionStrength &&
                config.PerspectiveEmissionColor == shaders.PerspectiveEmissionColor &&
                config.PerspectiveEmissionStrength == shaders.PerspectiveEmissionStrength &&
                config.SurfaceOccupancy == shaders.SurfaceOccupancy &&
                config.BloomIntensity == shaders.BloomIntensity &&
                config.VignetteIntensity == shaders.VignetteIntensity &&
                config.VignetteColor == shaders.VignetteColor &&
                config.VignetteSmoothness == shaders.VignetteSmoothness &&
                config.VignetteCenter == shaders.VignetteCenter &&
                config.ChromaticAberrationIntensity == shaders.ChromaticAberrationIntensity &&
                config.ColorGradingExposure == shaders.ColorGradingExposure &&
                config.ColorGradingFilter == shaders.ColorGradingFilter &&
                config.ColorGradingContrast == shaders.ColorGradingContrast &&
                config.ColorGradingSaturation == shaders.ColorGradingSaturation &&
                config.ColorGradingToneMapping == shaders.ColorGradingToneMapping &&
                config.ColorGradingToneMappingWhitePoint == shaders.ColorGradingToneMappingWhitePoint &&
                config.EigengrauIntensity == shaders.EigengrauIntensity &&
                config.EigengrauColor == shaders.EigengrauColor &&
                config.EigengrauDarknessThreshold == shaders.EigengrauDarknessThreshold &&
                config.EigengrauNoiseScale == shaders.EigengrauNoiseScale &&
                config.EigengrauAnimationSpeed == shaders.EigengrauAnimationSpeed &&
                config.MotionBlurIntensity == shaders.MotionBlurIntensity;
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

        private static void ValidateColor(Color value, string name)
        {
            ValidateFloat(value.r, 0f, float.MaxValue, $"{name}.r");
            ValidateFloat(value.g, 0f, float.MaxValue, $"{name}.g");
            ValidateFloat(value.b, 0f, float.MaxValue, $"{name}.b");
            ValidateFloat(value.a, 0f, float.MaxValue, $"{name}.a");
        }
    }
}
