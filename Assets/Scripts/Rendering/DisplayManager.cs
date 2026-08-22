#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Rendering
{
    public class DisplayManager : MonoBehaviour
    {
        [Inject]
        private IClientConfigManager _clientConfig = null!;

        private bool _isMutedInBackground;

        protected void Start()
        {
            ApplyDisplaySettings();
        }

        public void ApplyDisplaySettings()
        {
            if (_clientConfig == null || _clientConfig.Config == null)
            {
                return;
            }

            var config = _clientConfig.Config;

            // VSync & FrameRate
            QualitySettings.vSyncCount = config.VSync ? 1 : 0;
            Application.targetFrameRate = config.VSync ? -1 : config.TargetFrameRate;

            // Кап максимальной дельты кадра: долгий кадр на слабой машине не должен
            // превращаться в «спираль смерти» (гигантский скачок симуляции на
            // следующем кадре). Время кулдаунов идёт через Time.time — не затронуто.
            Time.maximumDeltaTime = 0.1f;

            // Resolution & Screen Mode
            if (config.ResolutionWidth > 0 && config.ResolutionHeight > 0)
            {
                var mode = (FullScreenMode)config.FullScreenMode;
                int refresh = config.RefreshRate > 0 ? config.RefreshRate : (int)Screen.currentResolution.refreshRateRatio.value;
                Screen.SetResolution(config.ResolutionWidth, config.ResolutionHeight, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refresh), denominator = 1 });
            }
        }

        public void SetResolution(int width, int height, FullScreenMode mode, int refreshRate = 60)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.Config.ResolutionWidth = width;
            _clientConfig.Config.ResolutionHeight = height;
            _clientConfig.Config.FullScreenMode = (int)mode;
            _clientConfig.Config.RefreshRate = refreshRate;
            _clientConfig.Save();

            Screen.SetResolution(width, height, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refreshRate), denominator = 1 });
        }

        public void SetVSync(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.Config.VSync = enabled;
            _clientConfig.Save();

            QualitySettings.vSyncCount = enabled ? 1 : 0;
            Application.targetFrameRate = enabled ? -1 : _clientConfig.Config.TargetFrameRate;
        }

        public void SetTargetFrameRate(int fps)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.Config.TargetFrameRate = fps;
            _clientConfig.Save();

            if (!QualitySettings.vSyncCount.Equals(1))
            {
                Application.targetFrameRate = fps;
            }
        }

        public void SetMuteInBackground(bool mute)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.Config.MuteAudioInBackground = mute;
            _clientConfig.Save();
        }

        public IReadOnlyList<Resolution> GetSupportedResolutions()
        {
            return Screen.resolutions;
        }

        protected void OnApplicationFocus(bool hasFocus)
        {
            if (_clientConfig?.Config != null && _clientConfig.Config.MuteAudioInBackground)
            {
                AudioListener.pause = !hasFocus;
                _isMutedInBackground = !hasFocus;
            }
            else if (_isMutedInBackground)
            {
                AudioListener.pause = false;
                _isMutedInBackground = false;
            }
        }
    }
}
