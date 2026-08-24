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

            // Display synchronization is independent from simulation/render throughput.
            // The client never imposes an application-level FPS ceiling.
            QualitySettings.vSyncCount = config.VSync ? 1 : 0;
            Application.targetFrameRate = -1;

            // Кап максимальной дельты кадра: долгий кадр на слабой машине не должен
            // превращаться в «спираль смерти» (гигантский скачок симуляции на
            // следующем кадре). Время кулдаунов идёт через Time.time — не затронуто.
            Time.maximumDeltaTime = 0.1f;

            // Resolution & Screen Mode
            if (config.ResolutionWidth > 0 && config.ResolutionHeight > 0)
            {
                var mode = NormalizeFullScreenMode((FullScreenMode)config.FullScreenMode);
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

            mode = NormalizeFullScreenMode(mode);
            _clientConfig.UpdateAndSave(config =>
            {
                config.ResolutionWidth = width;
                config.ResolutionHeight = height;
                config.FullScreenMode = (int)mode;
                config.RefreshRate = refreshRate;
            });

            Screen.SetResolution(width, height, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refreshRate), denominator = 1 });
        }

        public void SetVSync(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateAndSave(config => config.VSync = enabled);

            QualitySettings.vSyncCount = enabled ? 1 : 0;
            Application.targetFrameRate = -1;
        }

        public void SetMuteInBackground(bool mute)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateAndSave(config => config.MuteAudioInBackground = mute);
        }

        public IReadOnlyList<Resolution> GetSupportedResolutions()
        {
            return Screen.resolutions;
        }

        /// <summary>
        /// Unity на macOS не поддерживает ExclusiveFullScreen — единственный
        /// полноэкранный режим там FullScreenWindow. Маппим до вызова
        /// Screen.SetResolution, чтобы конфиг «exclusive» не ронял окно на Mac.
        /// </summary>
        private static FullScreenMode NormalizeFullScreenMode(FullScreenMode mode)
        {
#if UNITY_STANDALONE_OSX
            return mode == FullScreenMode.ExclusiveFullScreen
                ? FullScreenMode.FullScreenWindow
                : mode;
#else
            return mode;
#endif
        }

    }
}
