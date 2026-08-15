#nullable enable

using System;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core
{
    [CreateAssetMenu(fileName = "ProjectDefaults", menuName = "Fodinae/Project Defaults")]
    public sealed class ProjectDefaults : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField]
        private int _schemaVersion;
        [SerializeField]
        private ClientDefaultsGroup _client = new();
        [SerializeField]
        private LightingDefaultsGroup _lighting = new();

        public int SchemaVersion => _schemaVersion;

        public ProjectDefaultsSnapshot CreateSnapshot()
        {
            Validate();
            return new ProjectDefaultsSnapshot(
                _schemaVersion,
                Hash128.Compute(JsonUtility.ToJson(this)).ToString(),
                _client.CreateSnapshot(),
                _lighting.CreateSnapshot());
        }

        public void Validate()
        {
            if (_schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Project defaults schema {_schemaVersion} is unsupported; " +
                    $"expected {CurrentSchemaVersion}.");
            }

            _client.Validate();
            _lighting.Validate();
        }

        [Serializable]
        private sealed class ClientDefaultsGroup
        {
            [SerializeField]
            private float _masterVolume;
            [SerializeField]
            private float _sfxVolume;
            [SerializeField]
            private float _musicVolume;
            [SerializeField]
            private float _ambienceVolume;
            [SerializeField]
            private float _voiceVolume;
            [SerializeField]
            private float _uiVolume;
            [SerializeField]
            private float _uiScale;
            [SerializeField]
            private int _graphicsQuality;
            [SerializeField]
            private float _renderScale;
            [SerializeField]
            private int _vSyncCount;
            [SerializeField]
            private int _antiAliasing;

            public ClientDefaultsSnapshot CreateSnapshot()
            {
                return new ClientDefaultsSnapshot(
                    _masterVolume,
                    _sfxVolume,
                    _musicVolume,
                    _ambienceVolume,
                    _voiceVolume,
                    _uiVolume,
                    _uiScale,
                    _graphicsQuality,
                    _renderScale,
                    _vSyncCount,
                    _antiAliasing);
            }

            public void Validate()
            {
                ValidateRange(_masterVolume, 0f, 1f, nameof(_masterVolume));
                ValidateRange(_sfxVolume, 0f, 1f, nameof(_sfxVolume));
                ValidateRange(_musicVolume, 0f, 1f, nameof(_musicVolume));
                ValidateRange(_ambienceVolume, 0f, 1f, nameof(_ambienceVolume));
                ValidateRange(_voiceVolume, 0f, 1f, nameof(_voiceVolume));
                ValidateRange(_uiVolume, 0f, 1f, nameof(_uiVolume));
                ValidateRange(_uiScale, 0.5f, 2f, nameof(_uiScale));
                ValidateRange(_renderScale, 0.1f, 4f, nameof(_renderScale));
                ValidateRange(_graphicsQuality, 0, 3, nameof(_graphicsQuality));
                ValidateRange(_vSyncCount, 0, 4, nameof(_vSyncCount));
                ValidateRange(_antiAliasing, 0, 8, nameof(_antiAliasing));
            }
        }

        [Serializable]
        private sealed class LightingDefaultsGroup
        {
            [SerializeField]
            private TerrariaLightingEngine.QualityPreset _quality;
            [SerializeField]
            private bool _ambientOcclusionEnabled;
            [SerializeField]
            private bool _diffuseBounceEnabled;
            [SerializeField]
            private float _ambientIntensity;
            [SerializeField]
            private float _emissionScale;
            [SerializeField]
            private Color _ambientColor;
            [SerializeField]
            private Color _emptyExtinctionRgb;
            [SerializeField]
            private Color _solidExtinctionRgb;
            [SerializeField]
            private float _emptyExtinctionMultiplier;
            [SerializeField]
            private float _solidExtinctionMultiplier;
            [SerializeField]
            private float _bounceStrength;
            [SerializeField]
            private float _ambientOcclusionRadiusCells;
            [SerializeField]
            private float _ambientOcclusionStrength;
            [SerializeField]
            private float _maximumLightMultiplier;
            [SerializeField]
            private bool _enableFinalLightingClamp;
            [SerializeField]
            private float _transmittanceDebugDistanceCells;
            [SerializeField]
            private float _minimumTransmission;
            [SerializeField]
            private int _lightSafeBorder;
            [SerializeField]
            private float _dynamicLightIntensity;
            [SerializeField]
            private Color _dynamicLightColor;
            [SerializeField]
            private float _dynamicLightUpdatesPerSecond;

            public LightingDefaultsSnapshot CreateSnapshot()
            {
                return new LightingDefaultsSnapshot(
                    _quality,
                    _ambientOcclusionEnabled,
                    _diffuseBounceEnabled,
                    _ambientIntensity,
                    _emissionScale,
                    _ambientColor,
                    _emptyExtinctionRgb,
                    _solidExtinctionRgb,
                    _emptyExtinctionMultiplier,
                    _solidExtinctionMultiplier,
                    _bounceStrength,
                    _ambientOcclusionRadiusCells,
                    _ambientOcclusionStrength,
                    _maximumLightMultiplier,
                    _enableFinalLightingClamp,
                    _transmittanceDebugDistanceCells,
                    _minimumTransmission,
                    _lightSafeBorder,
                    _dynamicLightIntensity,
                    _dynamicLightColor,
                    _dynamicLightUpdatesPerSecond);
            }

            public void Validate()
            {
                ValidateRange((int)_quality, 0, 3, nameof(_quality));
                ValidateRange(_ambientIntensity, 0f, 1f, nameof(_ambientIntensity));
                ValidateRange(_emissionScale, 0.1f, 8f, nameof(_emissionScale));
                ValidateColor(_ambientColor, nameof(_ambientColor));
                ValidateColor(_emptyExtinctionRgb, nameof(_emptyExtinctionRgb));
                ValidateColor(_solidExtinctionRgb, nameof(_solidExtinctionRgb));
                ValidateRange(
                    _emptyExtinctionMultiplier,
                    0f,
                    2f,
                    nameof(_emptyExtinctionMultiplier));
                ValidateRange(
                    _solidExtinctionMultiplier,
                    0.25f,
                    2f,
                    nameof(_solidExtinctionMultiplier));
                ValidateRange(_bounceStrength, 0f, 1f, nameof(_bounceStrength));
                ValidateRange(
                    _ambientOcclusionRadiusCells,
                    0.5f,
                    8f,
                    nameof(_ambientOcclusionRadiusCells));
                ValidateRange(
                    _ambientOcclusionStrength,
                    0.1f,
                    8f,
                    nameof(_ambientOcclusionStrength));
                ValidateRange(
                    _maximumLightMultiplier,
                    0.25f,
                    LightingConfigLimits.MaximumLightMultiplier,
                    nameof(_maximumLightMultiplier));
                ValidateRange(
                    _transmittanceDebugDistanceCells,
                    2f,
                    32f,
                    nameof(_transmittanceDebugDistanceCells));
                ValidateRange(
                    _minimumTransmission,
                    0.0001f,
                    0.1f,
                    nameof(_minimumTransmission));
                ValidateRange(_lightSafeBorder, 0, 8, nameof(_lightSafeBorder));
                ValidateRange(
                    _dynamicLightIntensity,
                    0f,
                    4f,
                    nameof(_dynamicLightIntensity));
                ValidateColor(_dynamicLightColor, nameof(_dynamicLightColor));
                ValidateRange(
                    _dynamicLightUpdatesPerSecond,
                    1f,
                    LightingConfigLimits.DynamicLightUpdatesPerSecond,
                    nameof(_dynamicLightUpdatesPerSecond));
            }

            private static void ValidateColor(Color value, string name)
            {
                ValidateRange(value.r, 0f, float.MaxValue, $"{name}.r");
                ValidateRange(value.g, 0f, float.MaxValue, $"{name}.g");
                ValidateRange(value.b, 0f, float.MaxValue, $"{name}.b");
                ValidateRange(value.a, 0f, float.MaxValue, $"{name}.a");
            }
        }

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    $"Project default '{name}' must be finite and within [{minimum}, {maximum}].");
            }
        }

        private static void ValidateRange(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    $"Project default '{name}' must be within [{minimum}, {maximum}].");
            }
        }

    }
}
