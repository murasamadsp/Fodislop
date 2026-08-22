#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Audio.Backend
{
    /// <summary>
    /// Точка входа в аудио-домен — синглтон, висящий в DontDestroyOnLoad.
    ///
    /// Использует FmodAudioBackend для проигрывания FMOD Studio событий.
    /// Все события адресуются по строковому имени, соответствующему FMOD event path без prefix event:/.
    ///
    /// Пример: Play("sfx/dig") → FMOD event:/sfx/dig.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully catch startup exceptions to prevent game crash.")]
    [DefaultExecutionOrder(-10000)]
    public sealed class AudioSystem : MonoBehaviour, IAudioSystem
    {
        private const string TAG = "[AudioSystem]";
        private FmodAudioBackend _backend = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        private bool _configApplied;
        private bool _configWaitLogged;

        public bool IsInitialized => _backend != null;

        private void Awake()
        {
            _backend = new FmodAudioBackend();
            _backend.Initialize(this);
        }

        private void Start()
        {
            TryApplySavedBusVolumes();
        }

        private void Update()
        {
            if (!_configApplied)
            {
                TryApplySavedBusVolumes();
            }
        }

        private void TryApplySavedBusVolumes()
        {
            if (_configApplied)
            {
                return;
            }

            if (_clientConfig == null || _clientConfig.Config == null)
            {
                if (!_configWaitLogged)
                {
                    Debug.Log(
                        $"{TAG} Waiting for ClientConfigManager before applying audio settings.");
                    _configWaitLogged = true;
                }

                return;
            }

            ApplySavedBusVolumes();
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnAudioConfigurationChanged(bool deviceChanged)
        {
            if (deviceChanged)
            {
                Debug.Log($"{TAG} Default audio device was changed -> resetting audio backend");
                ResetBackend();
            }
        }

        public void ResetBackend()
        {
            try
            {
                _backend?.Shutdown();
                _backend = new FmodAudioBackend();
                _backend.Initialize(this);
                ApplySavedBusVolumes();
                Debug.Log($"{TAG} Audio backend successfully re-initialized after device change.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{TAG} Error resetting audio backend: {ex.Message}");
            }
        }

        public float GetBusVolume(AudioBusType type)
        {
            if (_backend == null)
            {
                if (!Application.isPlaying)
                {
                    return 1f;
                }

                throw new InvalidOperationException($"{TAG} Audio backend is not initialized");
            }

            return _backend.GetBusVolume(type);
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            if (_backend == null)
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                throw new InvalidOperationException($"{TAG} Audio backend is not initialized");
            }

            _backend.SetBusVolume(type, volume);
        }

        /// <summary>
        /// Динамическая загрузка доп. банков (фич/локаций) с CDN или локального хранилища.
        /// </summary>
        public async Cysharp.Threading.Tasks.UniTask<bool> EnsureBankLoadedAsync(string bankName)
        {
            if (_backend == null)
            {
                throw new InvalidOperationException($"{TAG} Audio backend is not initialized");
            }

            return await _backend.EnsureBankLoadedAsync(bankName);
        }

        /// <summary>
        /// Выгрузка банка из памяти (вызывать при выходе из зоны / завершении фичи).
        /// </summary>
        public void UnloadBank(string bankName)
        {
            if (_backend == null)
            {
                throw new InvalidOperationException($"{TAG} Audio backend is not initialized");
            }

            _backend.UnloadBank(bankName);
        }

        /// <summary>Воспроизвести событие по имени с опциональной 3D-позицией.</summary>
        public AudioPlaybackHandle? Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            if (IsKnownMissingFeatureBank(eventName))
            {
                return null;
            }

            var layer = overrideLayer ?? AudioLayer.SFXDefault();
            if (overrideVolume.HasValue)
            {
                layer.Volume = overrideVolume.Value;
            }

            var handle = _backend?.CreateVoice(eventName, layer, worldPosition);
            if (handle == null)
            {
                // Фиче-банки ("sfx/bz" → банк "sfx") подгружаются на лету по категории
                // события (часть исходного дизайна аудио-пайплайна) и звук дожимает ретраем.
                if (TryAutoLoadFeatureBank(eventName))
                {
                    LoadBankAndReplayAsync(eventName, layer, worldPosition, null).Forget();
                    return null;
                }

                // Missing optional audio, an unloaded sample, or an unavailable
                // feature bank is a valid no-audio state. The backend deliberately
                // returns null without blocking the game loop.
            }

            return handle;
        }

        /// <summary>Воспроизвести 3D-событие с нативной привязкой FMOD к GameObject (позиция/поворот следуют автоматически в C++).</summary>
        public AudioPlaybackHandle? PlayAttached(string eventName, GameObject targetGameObject, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (string.IsNullOrEmpty(eventName) || targetGameObject == null)
            {
                return null;
            }

            if (IsKnownMissingFeatureBank(eventName))
            {
                return null;
            }

            var layer = overrideLayer ?? AudioLayer.SFXDefault();
            if (overrideVolume.HasValue)
            {
                layer.Volume = overrideVolume.Value;
            }

            var handle = _backend?.CreateVoice(eventName, layer, null, targetGameObject);
            if (handle == null)
            {
                if (TryAutoLoadFeatureBank(eventName))
                {
                    LoadBankAndReplayAsync(eventName, layer, null, targetGameObject).Forget();
                    return null;
                }

                // Missing optional audio is intentionally a no-op.
            }

            return handle;
        }

        // ─── Фиче-банки по требованию ────────────────────────────────

        /// <summary>Извлекает имя фиче-банка из категории события: "sfx/bz" → "sfx".</summary>
        private static string? GetFeatureBankName(string eventName)
        {
            var name = eventName;
            if (name.StartsWith("event:/", System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(7);
            }

            if (name.StartsWith("snapshot:/", System.StringComparison.OrdinalIgnoreCase))
            {
                return null; // Снэпшоты живут в Master-банке.
            }

            int slash = name.IndexOf('/');
            return slash > 0 ? name.Substring(0, slash) : null;
        }

        /// <summary>Есть ли категория-банк, которую можно подгрузить (и ещё не подгружена).</summary>
        private bool TryAutoLoadFeatureBank(string eventName)
        {
            var bankName = GetFeatureBankName(eventName);
            if (string.IsNullOrEmpty(bankName))
            {
                return false;
            }

            if (IsKnownMissingFeatureBank(eventName))
            {
                return false;
            }

            if (_autoLoadInFlight || _autoLoadedBanks.Contains(bankName))
            {
                return false;
            }

            return true;
        }

        private bool IsKnownMissingFeatureBank(string eventName)
        {
            string? bankName = GetFeatureBankName(eventName);
            return _assetLoader is ClientAssetLoader loader &&
                bankName != null &&
                loader.IsKnownMissing($"banks/{bankName}.bank");
        }

        private bool _autoLoadInFlight;
        private readonly HashSet<string> _autoLoadedBanks = new();

        private async Cysharp.Threading.Tasks.UniTaskVoid LoadBankAndReplayAsync(
            string eventName, AudioLayer layer, Vector3? worldPosition, GameObject? targetGameObject)
        {
            var bankName = GetFeatureBankName(eventName);
            if (string.IsNullOrEmpty(bankName))
            {
                return;
            }

            _autoLoadInFlight = true;
            try
            {
                var ok = await EnsureBankLoadedAsync(bankName);
                if (!ok)
                {
                    // Bank not present in current environment (e.g. offline test mode without FMOD bank assets)
                    return;
                }

                _autoLoadedBanks.Add(bankName);
                _backend?.CreateVoice(eventName, layer, worldPosition, targetGameObject);
            }
            finally
            {
                _autoLoadInFlight = false;
            }
        }

        /// <summary>Воспроизвести FMOD Snapshot (например "snapshot:/cave_ambient").</summary>
        public AudioPlaybackHandle? PlaySnapshot(string snapshotPath)
        {
            if (string.IsNullOrEmpty(snapshotPath))
            {
                return null;
            }

            var handle = _backend?.PlaySnapshot(snapshotPath);

            // Snapshots are optional in offline/editor builds. A missing bank,
            // event, or unloaded sample is intentionally a no-op and must not
            // block gameplay or flood the console.
            return handle;
        }

        /// <summary>Установить значения глобального FMOD параметра в Studio (например "Depth", "Weather").</summary>
        public void SetGlobalParameter(string parameterName, float value)
        {
            _backend?.SetGlobalParameter(parameterName, value);
        }

        /// <summary>Воспроизвести 3D-событие на заданной позиции в мире.</summary>
        public AudioPlaybackHandle? PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null)
            => Play(eventName, worldPosition, layer, volume);

        /// <summary>Воспроизвести 2D-событие (без пространственного позиционирования).</summary>
        public AudioPlaybackHandle? Play2D(string eventName, AudioLayer? layer = null, float? volume = null)
            => Play(eventName, null, layer, volume);

        // ═══════════════════════════════════════════════════════════
        //  Protected Lifecycle Methods
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Применяет сохранённые локальные значения громкости для всех 6 шин FMOD Studio.
        /// </summary>
        public void ApplySavedBusVolumes()
        {
            if (_clientConfig == null || _clientConfig.Config == null)
            {
                return;
            }

            var config = _clientConfig.Config;
            SetBusVolume(AudioBusType.Master, config.MasterVolume);
            SetBusVolume(AudioBusType.SFX, config.SfxVolume);
            SetBusVolume(AudioBusType.Music, config.MusicVolume);
            SetBusVolume(AudioBusType.Voice, config.VoiceVolume);
            SetBusVolume(AudioBusType.Ambience, config.AmbienceVolume);
            SetBusVolume(AudioBusType.UI, config.UiVolume);
            _configApplied = true;
        }
    }
}
