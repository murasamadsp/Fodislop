#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Quality;
using UnityEngine;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Owns every runtime-configurable lighting knob and the persistence
    /// logic around it. LightingEngine delegates all Set* calls here
    /// and forwards the dirty flags back.
    /// </summary>
    internal sealed class LightingConfigHolder
    {
        private readonly IClientConfigManager _clientConfig;
        private LightingRuntimeConfig _runtimeConfig = null!;

        public bool AmbientOcclusionEnabled { get; private set; }
        public bool DiffuseBounceEnabled { get; private set; }
        public float AmbientIntensity { get; private set; }
        public float EmissionScale { get; private set; }
        public Color AmbientColor { get; private set; }
        public Color EmptyExtinctionRgb { get; private set; }
        public Color SolidExtinctionRgb { get; private set; }
        public float EmptyExtinctionMultiplier { get; private set; }
        public float SolidExtinctionMultiplier { get; private set; }
        public float BounceStrength { get; private set; }
        public float AmbientOcclusionRadiusCells { get; private set; }
        public float AmbientOcclusionStrength { get; private set; }
        public float MaximumLightMultiplier { get; private set; }
        public bool EnableFinalLightingClamp { get; private set; }
        public float TransmittanceDebugDistanceCells { get; private set; }
        public float MinimumTransmission { get; private set; }
        public int LightSafeBorder { get; private set; }
        public float DynamicLightUpdatesPerSecond { get; private set; }

        public void QueueSave()
        {
            _savePending = true;
            _saveTime = Time.unscaledTime + 0.25f;
        }

        public bool TrySave(float currentTime)
        {
            if (_savePending && currentTime >= _saveTime)
            {
                _savePending = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Writes a pending change out immediately instead of waiting for the
        /// debounce window. Shutdown paths need this: a change made in the last
        /// quarter second before quit would otherwise never reach disk.
        /// </summary>
        public void ForceSave()
        {
            if (!_savePending)
            {
                return;
            }

            _savePending = false;
            _clientConfig.Save();
        }

        public void ApplyToClientConfig()
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "LightingEngine requires an initialized ClientConfig.");
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.AmbientOcclusionEnabled = _runtimeConfig.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = _runtimeConfig.DiffuseBounceEnabled;
            config.AmbientIntensity = _runtimeConfig.AmbientIntensity;
            config.EmissionScale = _runtimeConfig.EmissionScale;
            config.AmbientColor = _runtimeConfig.AmbientColor;
            config.EmptyExtinctionRgb = _runtimeConfig.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = _runtimeConfig.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = _runtimeConfig.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = _runtimeConfig.SolidExtinctionMultiplier;
            config.BounceStrength = _runtimeConfig.BounceStrength;
            config.AmbientOcclusionRadiusCells = _runtimeConfig.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = _runtimeConfig.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = _runtimeConfig.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = _runtimeConfig.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = _runtimeConfig.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = _runtimeConfig.MinimumTransmission;
            config.LightSafeBorder = _runtimeConfig.LightSafeBorder;
            config.DynamicLightIntensity = _runtimeConfig.DynamicLightIntensity;
            config.DynamicLightColor = _runtimeConfig.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = _runtimeConfig.DynamicLightUpdatesPerSecond;
        }

        public string ConfigFilePath => _clientConfig.ConfigFilePath;
        public LightingRuntimeConfig RuntimeConfig => _runtimeConfig;
        public float DynamicLightIntensity => _runtimeConfig.DynamicLightIntensity;
        public Color DynamicLightColor => _runtimeConfig.DynamicLightColor;

        private bool _savePending;
        private float _saveTime;

        public LightingConfigHolder(IClientConfigManager clientConfig)
        {
            _clientConfig = clientConfig;
        }

        public void Load()
        {
            _runtimeConfig = CreateConfigFromClientConfig();
            ApplyRuntimeConfig(_runtimeConfig);
        }

        public void ApplyProjectDefaults(LightingDefaultsSnapshot defaults)
        {
            AmbientOcclusionEnabled = defaults.AmbientOcclusionEnabled;
            DiffuseBounceEnabled = defaults.DiffuseBounceEnabled;
            AmbientIntensity = defaults.AmbientIntensity;
            EmissionScale = defaults.EmissionScale;
            AmbientColor = defaults.AmbientColor;
            EmptyExtinctionRgb = defaults.EmptyExtinctionRgb;
            SolidExtinctionRgb = defaults.SolidExtinctionRgb;
            EmptyExtinctionMultiplier = defaults.EmptyExtinctionMultiplier;
            SolidExtinctionMultiplier = defaults.SolidExtinctionMultiplier;
            BounceStrength = defaults.BounceStrength;
            AmbientOcclusionRadiusCells = defaults.AmbientOcclusionRadiusCells;
            AmbientOcclusionStrength = defaults.AmbientOcclusionStrength;
            MaximumLightMultiplier = defaults.MaximumLightMultiplier;
            EnableFinalLightingClamp = defaults.EnableFinalLightingClamp;
            TransmittanceDebugDistanceCells = defaults.TransmittanceDebugDistanceCells;
            MinimumTransmission = defaults.MinimumTransmission;
            LightSafeBorder = defaults.LightSafeBorder;
            DynamicLightUpdatesPerSecond = defaults.DynamicLightUpdatesPerSecond;
        }

        public void ApplyLightingDefaultsToClientConfig(LightingDefaultsSnapshot defaults)
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "LightingEngine requires an initialized ClientConfig.");
            config.AmbientOcclusionEnabled = defaults.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = defaults.DiffuseBounceEnabled;
            config.AmbientIntensity = defaults.AmbientIntensity;
            config.EmissionScale = defaults.EmissionScale;
            config.AmbientColor = defaults.AmbientColor;
            config.EmptyExtinctionRgb = defaults.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = defaults.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = defaults.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = defaults.SolidExtinctionMultiplier;
            config.BounceStrength = defaults.BounceStrength;
            config.AmbientOcclusionRadiusCells = defaults.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = defaults.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = defaults.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = defaults.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = defaults.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = defaults.MinimumTransmission;
            config.LightSafeBorder = defaults.LightSafeBorder;
            config.DynamicLightIntensity = defaults.DynamicLightIntensity;
            config.DynamicLightColor = defaults.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = defaults.DynamicLightUpdatesPerSecond;
        }

        public bool SetAmbientOcclusionEnabled(bool enabled)
        {
            if (AmbientOcclusionEnabled == enabled)
            {
                return false;
            }

            AmbientOcclusionEnabled = enabled;
            _runtimeConfig.AmbientOcclusionEnabled = enabled;
            QueueSave();
            return true;
        }

        public bool SetDiffuseBounceEnabled(bool enabled)
        {
            if (DiffuseBounceEnabled == enabled)
            {
                return false;
            }

            DiffuseBounceEnabled = enabled;
            _runtimeConfig.DiffuseBounceEnabled = enabled;
            QueueSave();
            return true;
        }

        public bool SetAmbientIntensity(float value)
        {
            if (!TryClamp(AmbientIntensity, value, 0f, 1f, out float clamped))
            {
                return false;
            }

            AmbientIntensity = clamped;
            _runtimeConfig.AmbientIntensity = clamped;
            QueueSave();
            return true;
        }

        public bool SetAmbientColor(Color value)
        {
            if (!TrySanitize(AmbientColor, value, out Color sanitized))
            {
                return false;
            }

            AmbientColor = sanitized;
            _runtimeConfig.AmbientColor = sanitized;
            QueueSave();
            return true;
        }

        public bool SetEmissionScale(float value)
        {
            if (!TryClamp(EmissionScale, value, 0.1f, 8f, out float clamped))
            {
                return false;
            }

            EmissionScale = clamped;
            _runtimeConfig.EmissionScale = clamped;
            QueueSave();
            return true;
        }

        public bool SetEmptyExtinctionColor(Color value)
        {
            if (!TrySanitize(EmptyExtinctionRgb, value, out Color sanitized))
            {
                return false;
            }

            EmptyExtinctionRgb = sanitized;
            _runtimeConfig.EmptyExtinctionRgb = sanitized;
            QueueSave();
            return true;
        }

        public bool SetSolidExtinctionColor(Color value)
        {
            if (!TrySanitize(SolidExtinctionRgb, value, out Color sanitized))
            {
                return false;
            }

            SolidExtinctionRgb = sanitized;
            _runtimeConfig.SolidExtinctionRgb = sanitized;
            QueueSave();
            return true;
        }

        public bool SetFinalLightingClampEnabled(bool enabled)
        {
            if (EnableFinalLightingClamp == enabled)
            {
                return false;
            }

            EnableFinalLightingClamp = enabled;
            _runtimeConfig.EnableFinalLightingClamp = enabled;
            QueueSave();
            return true;
        }

        public bool SetEmptyExtinctionMultiplier(float value)
        {
            if (!TryClamp(EmptyExtinctionMultiplier, value, 0f, 2f, out float clamped))
            {
                return false;
            }

            EmptyExtinctionMultiplier = clamped;
            _runtimeConfig.EmptyExtinctionMultiplier = clamped;
            QueueSave();
            return true;
        }

        public bool SetSolidExtinctionMultiplier(float value)
        {
            if (!TryClamp(SolidExtinctionMultiplier, value, 0.25f, 2f, out float clamped))
            {
                return false;
            }

            SolidExtinctionMultiplier = clamped;
            _runtimeConfig.SolidExtinctionMultiplier = clamped;
            QueueSave();
            return true;
        }

        public bool SetBounceStrength(float value)
        {
            if (!TryClamp(BounceStrength, value, 0f, 1f, out float clamped))
            {
                return false;
            }

            BounceStrength = clamped;
            _runtimeConfig.BounceStrength = clamped;
            QueueSave();
            return true;
        }

        public bool SetAmbientOcclusionRadius(float value)
        {
            if (!TryClamp(AmbientOcclusionRadiusCells, value, 0.5f, 8f, out float clamped))
            {
                return false;
            }

            AmbientOcclusionRadiusCells = clamped;
            _runtimeConfig.AmbientOcclusionRadiusCells = clamped;
            QueueSave();
            return true;
        }

        public bool SetAmbientOcclusionStrength(float value)
        {
            if (!TryClamp(AmbientOcclusionStrength, value, 0.1f, 8f, out float clamped))
            {
                return false;
            }

            AmbientOcclusionStrength = clamped;
            _runtimeConfig.AmbientOcclusionStrength = clamped;
            QueueSave();
            return true;
        }

        public bool SetMaximumLightMultiplier(float value)
        {
            if (!TryClamp(
                MaximumLightMultiplier,
                value,
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier,
                out float clamped))
            {
                return false;
            }

            MaximumLightMultiplier = clamped;
            _runtimeConfig.MaximumLightMultiplier = clamped;
            QueueSave();
            return true;
        }

        public bool SetTransmittanceDebugDistance(float value)
        {
            if (!TryClamp(TransmittanceDebugDistanceCells, value, 2f, 32f, out float clamped))
            {
                return false;
            }

            TransmittanceDebugDistanceCells = clamped;
            _runtimeConfig.TransmittanceDebugDistanceCells = clamped;
            QueueSave();
            return true;
        }

        public bool SetMinimumTransmission(float value)
        {
            if (!TryClamp(MinimumTransmission, value, 0.0001f, 0.1f, out float clamped))
            {
                return false;
            }

            MinimumTransmission = clamped;
            _runtimeConfig.MinimumTransmission = clamped;
            QueueSave();
            return true;
        }

        public bool SetLightSafeBorder(float value)
        {
            int border = Mathf.RoundToInt(Mathf.Clamp(value, 0f, 8f));
            if (LightSafeBorder == border)
            {
                return false;
            }

            LightSafeBorder = border;
            _runtimeConfig.LightSafeBorder = border;
            QueueSave();
            return true;
        }

        public bool SetDynamicLightSettings(float intensity, Color color)
        {
            float clampedIntensity = Mathf.Clamp(intensity, 0f, 4f);
            Color sanitized = new(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                1f);
            if (Mathf.Approximately(_runtimeConfig.DynamicLightIntensity, clampedIntensity) &&
                _runtimeConfig.DynamicLightColor == sanitized)
            {
                return false;
            }

            _runtimeConfig.DynamicLightIntensity = clampedIntensity;
            _runtimeConfig.DynamicLightColor = sanitized;
            QueueSave();
            return true;
        }

        public bool SetDynamicLightUpdatesPerSecond(float value)
        {
            if (!TryClamp(
                DynamicLightUpdatesPerSecond,
                value,
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond,
                out float clamped))
            {
                return false;
            }

            DynamicLightUpdatesPerSecond = clamped;
            _runtimeConfig.DynamicLightUpdatesPerSecond = clamped;
            QueueSave();
            return true;
        }

        public void ApplyClientConfig()
        {
            _runtimeConfig = CreateConfigFromClientConfig();
            ApplyRuntimeConfig(_runtimeConfig);
        }

        public void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "LightingEngine requires an initialized ClientConfig.");
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.AmbientOcclusionEnabled = _runtimeConfig.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = _runtimeConfig.DiffuseBounceEnabled;
            config.AmbientIntensity = _runtimeConfig.AmbientIntensity;
            config.EmissionScale = _runtimeConfig.EmissionScale;
            config.AmbientColor = _runtimeConfig.AmbientColor;
            config.EmptyExtinctionRgb = _runtimeConfig.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = _runtimeConfig.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = _runtimeConfig.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = _runtimeConfig.SolidExtinctionMultiplier;
            config.BounceStrength = _runtimeConfig.BounceStrength;
            config.AmbientOcclusionRadiusCells = _runtimeConfig.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = _runtimeConfig.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = _runtimeConfig.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = _runtimeConfig.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = _runtimeConfig.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = _runtimeConfig.MinimumTransmission;
            config.LightSafeBorder = _runtimeConfig.LightSafeBorder;
            config.DynamicLightIntensity = _runtimeConfig.DynamicLightIntensity;
            config.DynamicLightColor = _runtimeConfig.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = _runtimeConfig.DynamicLightUpdatesPerSecond;
        }

        private LightingRuntimeConfig CreateConfigFromClientConfig()
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "Cannot create lighting runtime config: ClientConfig is not initialized.");

            LightingRuntimeConfig runtimeConfig = new()
            {
                Schema = LightingRuntimeConfig.SchemaId,
                Version = LightingRuntimeConfig.CurrentVersion,
                AmbientOcclusionEnabled = config.AmbientOcclusionEnabled,
                DiffuseBounceEnabled = config.DiffuseBounceEnabled,
                AmbientIntensity = Mathf.Clamp(config.AmbientIntensity, 0f, 1f),
                EmissionScale = Mathf.Clamp(config.EmissionScale <= 0f ? 1.0f : config.EmissionScale, 0.1f, 8f),
                AmbientColor = config.AmbientColor,
                EmptyExtinctionRgb = config.EmptyExtinctionRgb,
                SolidExtinctionRgb = config.SolidExtinctionRgb,
                EmptyExtinctionMultiplier = Mathf.Clamp(config.EmptyExtinctionMultiplier, 0f, 2f),
                SolidExtinctionMultiplier = Mathf.Clamp(config.SolidExtinctionMultiplier <= 0f ? 1.0f : config.SolidExtinctionMultiplier, 0.25f, 2f),
                BounceStrength = Mathf.Clamp(config.BounceStrength, 0f, 1f),
                AmbientOcclusionRadiusCells = Mathf.Clamp(config.AmbientOcclusionRadiusCells <= 0f ? 2.0f : config.AmbientOcclusionRadiusCells, 0.5f, 8f),
                AmbientOcclusionStrength = Mathf.Clamp(config.AmbientOcclusionStrength <= 0f ? 1.0f : config.AmbientOcclusionStrength, 0.1f, 8f),
                MaximumLightMultiplier = Mathf.Clamp(config.MaximumLightMultiplier <= 0f ? 1.5f : config.MaximumLightMultiplier, 0.25f, LightingConfigLimits.MaximumLightMultiplier),
                EnableFinalLightingClamp = config.EnableFinalLightingClamp,
                TransmittanceDebugDistanceCells = Mathf.Clamp(config.TransmittanceDebugDistanceCells <= 0f ? 16f : config.TransmittanceDebugDistanceCells, 2f, 32f),
                MinimumTransmission = Mathf.Clamp(config.MinimumTransmission <= 0f ? 0.01f : config.MinimumTransmission, 0.0001f, 0.1f),
                LightSafeBorder = Mathf.Clamp(config.LightSafeBorder, 0, 8),
                DynamicLightIntensity = Mathf.Clamp(config.DynamicLightIntensity, 0f, 4f),
                DynamicLightColor = config.DynamicLightColor,
                DynamicLightUpdatesPerSecond = Mathf.Clamp(config.DynamicLightUpdatesPerSecond <= 0f ? 30f : config.DynamicLightUpdatesPerSecond, 1f, LightingConfigLimits.DynamicLightUpdatesPerSecond),
            };
            runtimeConfig.Validate();
            return runtimeConfig;
        }

        private void ApplyRuntimeConfig(LightingRuntimeConfig config)
        {
            AmbientOcclusionEnabled = config.AmbientOcclusionEnabled;
            DiffuseBounceEnabled = config.DiffuseBounceEnabled;
            AmbientIntensity = !Application.isPlaying && config.AmbientIntensity < 0.4f ? 0.4f : config.AmbientIntensity;
            EmissionScale = config.EmissionScale;
            AmbientColor = !Application.isPlaying && (config.AmbientColor.r + config.AmbientColor.g + config.AmbientColor.b < 0.2f)
                ? new Color(0.8f, 0.85f, 0.95f, 1f)
                : config.AmbientColor;
            EmptyExtinctionRgb = config.EmptyExtinctionRgb;
            SolidExtinctionRgb = config.SolidExtinctionRgb;
            EmptyExtinctionMultiplier = config.EmptyExtinctionMultiplier;
            SolidExtinctionMultiplier = config.SolidExtinctionMultiplier;
            BounceStrength = config.BounceStrength;
            AmbientOcclusionRadiusCells = config.AmbientOcclusionRadiusCells;
            AmbientOcclusionStrength = config.AmbientOcclusionStrength;
            MaximumLightMultiplier = config.MaximumLightMultiplier;
            EnableFinalLightingClamp = config.EnableFinalLightingClamp;
            TransmittanceDebugDistanceCells = config.TransmittanceDebugDistanceCells;
            MinimumTransmission = config.MinimumTransmission;
            LightSafeBorder = config.LightSafeBorder;
            DynamicLightUpdatesPerSecond = config.DynamicLightUpdatesPerSecond;
        }

        /// <summary>
        /// True when <paramref name="value"/>, once clamped, actually differs
        /// from the live value - the caller then owns assigning both the
        /// property and the serialized field.
        /// </summary>
        private static bool TryClamp(
            float current,
            float value,
            float minimum,
            float maximum,
            out float clamped)
        {
            clamped = Mathf.Clamp(value, minimum, maximum);
            return !Mathf.Approximately(current, clamped);
        }

        private static bool TrySanitize(Color current, Color value, out Color sanitized)
        {
            sanitized = new Color(
                Mathf.Max(0f, value.r),
                Mathf.Max(0f, value.g),
                Mathf.Max(0f, value.b),
                Mathf.Max(0f, value.a));
            return current != sanitized;
        }
    }
}
