#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Fodinae.Rendering
{
    public class DisplayManager : MonoBehaviour
    {
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        protected void Start()
        {
            ApplyDisplaySettings();
        }

        public static void ApplyInitialSettings(DisplaySettings display)
        {
            if (display == null)
            {
                return;
            }

            AutoDetectDisplayCapabilities(display);
            SanitizeCalibration(display);
            HDROutput.SetEnabled(display.HDREnabled);
            PostProcessRuntimeState.SetDisplayCalibration(
                display.Gamma,
                display.PaperWhiteNits,
                display.PeakBrightnessNits);

            QualitySettings.vSyncCount = display.VSync ? 1 : 0;
            Application.targetFrameRate = display.TargetFrameRate;
            Time.maximumDeltaTime = 0.1f;

            if (display.ResolutionWidth > 0 && display.ResolutionHeight > 0)
            {
                var mode = NormalizeFullScreenMode((FullScreenMode)display.FullScreenMode);
                int refresh = display.RefreshRate > 0 ? display.RefreshRate : (int)Screen.currentResolution.refreshRateRatio.value;
                Screen.SetResolution(display.ResolutionWidth, display.ResolutionHeight, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refresh), denominator = 1 });
            }
        }

        public void ApplyDisplaySettings()
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            DisplaySettings display = _clientConfig.Config.Display;
            ApplyInitialSettings(display);
            ApplyPixelSampling(display.PixelSampling);
            HDROutput.ConfigureCamera(_gameplayCamera.Camera);
        }

        /// <summary>
        /// Переключает режим укладки мира на пиксельную сетку.
        /// </summary>
        public void SetPixelSamplingMode(PixelSamplingMode mode)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.PixelSampling = mode);
            ApplyPixelSampling(mode);
            Debug.Log($"[DisplayManager] SetPixelSamplingMode: {mode}");
        }

        /// <summary>
        /// Раздаёт режим шейдерам.
        /// </summary>
        /// <remarks>
        /// Через глобальную переменную шейдера, а не через материалы:
        /// террейн и сущности мира рисуются разными материалами, часть из
        /// которых создаётся в рантайме, и обойти их все означало бы
        /// завести реестр материалов ради одного тумблера.
        ///
        /// Камера читает режим сама: ей нужен не флаг, а решение, округлять
        /// ли размер, и это её собственная работа.
        /// </remarks>
        private static void ApplyPixelSampling(PixelSamplingMode mode)
        {
            Shader.SetGlobalFloat(
                _PixelArtFilteringProperty,
                PixelSamplingRules.FiltersTexelEdges(mode) ? 1f : 0f);
        }

        private static readonly int _PixelArtFilteringProperty = Shader.PropertyToID("_PixelArtFiltering");

        public void SetResolution(int width, int height, FullScreenMode mode, int refreshRate = 60)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            mode = NormalizeFullScreenMode(mode);
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.ResolutionWidth = width;
                display.ResolutionHeight = height;
                display.FullScreenMode = (int)mode;
                display.RefreshRate = refreshRate;
            });

            Screen.SetResolution(width, height, mode, new RefreshRate { numerator = (uint)Mathf.Max(1, refreshRate), denominator = 1 });
            Debug.Log($"[DisplayManager] SetResolution: {width}x{height} @ {refreshRate}Hz (Mode={mode})");
        }

        public void SetVSync(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            _clientConfig.UpdateSection(config => config.Display, display => display.VSync = enabled);

            QualitySettings.vSyncCount = enabled ? 1 : 0;
            Application.targetFrameRate = _clientConfig.Config.Display.TargetFrameRate;
            Debug.Log($"[DisplayManager] SetVSync: {enabled} (TargetFPS={_clientConfig.Config.Display.TargetFrameRate})");
        }

        /// <summary>
        /// Applies the HDR preference and reports what the display did with it.
        /// </summary>
        /// <remarks>
        /// A refused request must not stay written in the config. Otherwise the
        /// settings toggle keeps reading back "on" from a preference the display
        /// never honoured, and the player is told the opposite of what they see.
        /// The one refusal that is NOT rolled back is an absent HDR display:
        /// availability is reported late and can appear after a monitor change,
        /// and HDROutputReconciler completes the request when it does.
        /// </remarks>
        public HDROutput.ApplyRequestResult SetHDREnabled(bool enabled)
        {
            if (_clientConfig?.Config == null)
            {
                return HDROutput.ApplyRequestResult.RejectedUnsupported;
            }

            bool previous = _clientConfig.Config.Display.HDREnabled;
            _clientConfig.UpdateSection(config => config.Display, display => display.HDREnabled = enabled);

            HDROutput.ApplyRequestResult result = HDROutput.SetEnabled(enabled);
            if (result == HDROutput.ApplyRequestResult.RejectedNotSwitchable)
            {
                _clientConfig.UpdateSection(config => config.Display, display => display.HDREnabled = previous);
                HDROutput.SetEnabled(previous);
                HDROutput.ConfigureCamera(_gameplayCamera.Camera);
                Debug.LogWarning(
                    "[HDR] Display is HDR-capable but not runtime-switchable; " +
                    $"the preference stays at {previous}. Switch HDR in the OS display settings.");
                return result;
            }

            if (result == HDROutput.ApplyRequestResult.RejectedUnsupported)
            {
                Debug.LogWarning(
                    "[HDR] No HDR-capable display is reported yet; the preference is kept " +
                    "and applied by HDROutputReconciler once one appears.");
            }

            HDROutput.ConfigureCamera(_gameplayCamera.Camera);
            Debug.Log($"[DisplayManager] SetHDREnabled: {enabled} (Result={result})");
            return result;
        }

        public void SetGamma(float gamma)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float sanitized = FiniteClamp(
                gamma,
                DisplaySettings.GammaMin,
                DisplaySettings.GammaMax,
                DisplaySettings.DefaultGamma);
            _clientConfig.UpdateSection(config => config.Display, display => display.Gamma = sanitized);
            PostProcessRuntimeState.SetDisplayCalibration(
                sanitized,
                _clientConfig.Config.Display.PaperWhiteNits,
                _clientConfig.Config.Display.PeakBrightnessNits);
            Debug.Log($"[DisplayManager] SetGamma: {sanitized}");
        }

        public void SetPaperWhiteNits(float paperWhiteNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float sanitizedPaperWhite = FiniteClamp(
                paperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            float sanitizedPeak = Mathf.Max(
                sanitizedPaperWhite,
                FiniteClamp(
                    _clientConfig.Config.Display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.PaperWhiteNits = sanitizedPaperWhite;
                display.PeakBrightnessNits = sanitizedPeak;
            });
            PostProcessRuntimeState.SetDisplayCalibration(
                _clientConfig.Config.Display.Gamma,
                sanitizedPaperWhite,
                sanitizedPeak);
            Debug.Log(
                $"[DisplayManager] SetPaperWhiteNits: {sanitizedPaperWhite} " +
                $"(Peak={sanitizedPeak})");
        }

        public void SetPeakBrightnessNits(float peakBrightnessNits)
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            float paperWhite = FiniteClamp(
                _clientConfig.Config.Display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            float sanitizedPeak = Mathf.Max(
                paperWhite,
                FiniteClamp(
                    peakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
            _clientConfig.UpdateSection(config => config.Display, display =>
            {
                display.PaperWhiteNits = paperWhite;
                display.PeakBrightnessNits = sanitizedPeak;
            });
            PostProcessRuntimeState.SetDisplayCalibration(
                _clientConfig.Config.Display.Gamma,
                paperWhite,
                sanitizedPeak);
            Debug.Log($"[DisplayManager] SetPeakBrightnessNits: {sanitizedPeak}");
        }

        public static void AutoDetectDisplayCapabilities(DisplaySettings display)
        {
            HDROutput.AutoDetectDisplayCapabilities(display);
        }

        private static void SanitizeCalibration(DisplaySettings display)
        {
            display.Gamma = FiniteClamp(
                display.Gamma,
                DisplaySettings.GammaMin,
                DisplaySettings.GammaMax,
                DisplaySettings.DefaultGamma);
            display.PaperWhiteNits = FiniteClamp(
                display.PaperWhiteNits,
                DisplaySettings.PaperWhiteMin,
                DisplaySettings.PaperWhiteMax,
                DisplaySettings.DefaultPaperWhite);
            display.PeakBrightnessNits = Mathf.Max(
                display.PaperWhiteNits,
                FiniteClamp(
                    display.PeakBrightnessNits,
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    DisplaySettings.DefaultPeakBrightness));
        }

        private static float FiniteClamp(
            float value,
            float minimum,
            float maximum,
            float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : Mathf.Clamp(value, minimum, maximum);

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
