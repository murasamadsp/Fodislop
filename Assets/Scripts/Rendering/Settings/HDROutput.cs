#nullable enable

namespace Fodinae.Rendering;

using System;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Owns the boundary between the scene-linear HDR render and the operating
/// system's HDR display surface.
/// </summary>
public static class HDROutput
{
    private static HDRDiagnosticState _lastDiagnosticState;
    private static bool _hasDiagnosticState;
    private static bool _enabled;
    private static bool _preferenceInitialized;

    public static bool Enabled => _preferenceInitialized && _enabled;

    private readonly record struct HDRDiagnosticState(
        bool Available,
        bool Active,
        bool ChangeRequested,
        HDRDisplaySupportFlags SupportFlags,
        ColorGamut Gamut,
        float PaperWhiteNits,
        int MinToneMapLuminance,
        int MaxToneMapLuminance);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDiagnostics()
    {
        _lastDiagnosticState = default;
        _hasDiagnosticState = false;
        _enabled = false;
        _preferenceInitialized = false;
    }

    public enum ApplyRequestResult
    {
        /// <summary>Запрос применён, дисплей поставлен в режим <c>enabled</c>.</summary>
        Applied,

        /// <summary>Запрос отправлен ранее и ещё в полёте; повторный вызов проигнорирован.</summary>
        AlreadyPending,

        /// <summary>Дисплей не HDR-capable в принципе (нет <c>HDROutputSettings.available</c>).</summary>
        RejectedUnsupported,

        /// <summary>Дисплей HDR-capable, но без <c>RuntimeSwitchable</c> флага — переключение невозможно.</summary>
        RejectedNotSwitchable,
    }

    public static void AutoDetectDisplayCapabilities(DisplaySettings display)
    {
        HDROutputSettings output = HDROutputSettings.main;
        if (output.available)
        {
            if (display.PaperWhiteNits <= 10f && output.paperWhiteNits > 10f)
            {
                display.PaperWhiteNits = Mathf.Clamp(
                    output.paperWhiteNits,
                    DisplaySettings.PaperWhiteMin,
                    DisplaySettings.PaperWhiteMax);
            }

            if (display.PeakBrightnessNits <= 100f && output.maxToneMapLuminance > 100)
            {
                display.PeakBrightnessNits = Mathf.Max(
                    display.PaperWhiteNits,
                    Mathf.Clamp(
                        output.maxToneMapLuminance,
                        DisplaySettings.PeakBrightnessMin,
                        DisplaySettings.PeakBrightnessMax));
            }
        }
    }

    public static ApplyRequestResult SetEnabled(bool enabled)
    {
        // Store intent before probing the display. Availability can be
        // reported late (for example after a scene or display change),
        // and Refresh must still be able to complete the request.
        _enabled = enabled;
        _preferenceInitialized = true;

        HDROutputSettings output = HDROutputSettings.main;
        if (!output.available)
        {
            LogDiagnostics(output);
            return ApplyRequestResult.RejectedUnsupported;
        }

        if (!output.HDRModeChangeRequested && enabled == output.active)
        {
            LogDiagnostics(output);
            return ApplyRequestResult.Applied;
        }

        bool runtimeSwitchable =
            (SystemInfo.hdrDisplaySupportFlags &
                HDRDisplaySupportFlags.RuntimeSwitchable) != 0;
        if (!runtimeSwitchable)
        {
            LogDiagnostics(output);
            return ApplyRequestResult.RejectedNotSwitchable;
        }

        if (output.HDRModeChangeRequested)
        {
            LogDiagnostics(output);
            return ApplyRequestResult.AlreadyPending;
        }

        // Request a switch only when the current state differs from
        // the user request, otherwise we keep spamming
        // RequestHDRModeChange every toggle reset.
        if (enabled != output.active)
        {
            output.RequestHDRModeChange(enabled);
        }

        LogDiagnostics(output);
        return ApplyRequestResult.Applied;
    }

    public static void Reconcile()
    {
        HDROutputSettings output = HDROutputSettings.main;
        if (!output.available)
        {
            LogDiagnostics(output);
            return;
        }

        if (_preferenceInitialized && Enabled != output.active &&
            (SystemInfo.hdrDisplaySupportFlags &
                HDRDisplaySupportFlags.RuntimeSwitchable) != 0 &&
            !output.HDRModeChangeRequested)
        {
            output.RequestHDRModeChange(Enabled);
        }

        LogDiagnostics(output);
    }

    private static void LogDiagnostics(HDROutputSettings output)
    {
        bool available = output.available;
        var state = new HDRDiagnosticState(
            available,
            available && output.active,
            available && output.HDRModeChangeRequested,
            SystemInfo.hdrDisplaySupportFlags,
            available ? output.displayColorGamut : default,
            available ? output.paperWhiteNits : 0f,
            available ? output.minToneMapLuminance : 0,
            available ? output.maxToneMapLuminance : 0);
        if (_hasDiagnosticState && state == _lastDiagnosticState)
        {
            return;
        }

        _lastDiagnosticState = state;
        _hasDiagnosticState = true;
        Debug.Log(
            "[HDR] " +
            $"available={state.Available}, active={state.Active}, " +
            $"changeRequested={state.ChangeRequested}, " +
            $"supportFlags={state.SupportFlags}, gamut={state.Gamut}, " +
            $"paperWhite={state.PaperWhiteNits:F1} nits, " +
            $"min={state.MinToneMapLuminance} nits, " +
            $"max={state.MaxToneMapLuminance} nits.");
    }

    public static void AppendDebugInfo(StringBuilder builder, Camera? camera)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        HDROutputSettings output = HDROutputSettings.main;
        bool available = output.available;
        bool active = available && output.active;
        bool changeRequested = available && output.HDRModeChangeRequested;
        ColorGamut gamut = available ? output.displayColorGamut : default;
        float paperWhiteNits = available ? output.paperWhiteNits : 0f;
        int minNits = available ? output.minToneMapLuminance : 0;
        int maxNits = available ? output.maxToneMapLuminance : 0;
        string status = !Enabled
            ? "DISABLED"
            : active
                ? "ACTIVE"
                : available ? "AVAILABLE / INACTIVE" : "UNAVAILABLE";
        builder.Append("<b>[HDR: ").Append(status).Append("]</b>\n")
            .Append("Enabled in settings: ").Append(Enabled).Append('\n')
            .Append("Available: ").Append(available)
            .Append(" | Active: ").Append(active)
            .Append(" | Requested: ").Append(changeRequested).Append('\n')
            .Append("Support: ").Append(SystemInfo.hdrDisplaySupportFlags)
            .Append(" | Gamut: ").Append(gamut).Append('\n')
            .Append("Luminance: ").Append(minNits)
            .Append(" / ").Append(paperWhiteNits.ToString("F1"))
            .Append(" / ").Append(maxNits)
            .Append(" nits (min / paper / OS max)\n");

        if (camera == null)
        {
            builder.Append("Display camera: MISSING\n\n");
            return;
        }

        builder.Append("Camera HDR buffer: ").Append(camera.allowHDR);
        if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            builder.Append(" | HDR output: ").Append(cameraData.allowHDROutput)
                .Append(" | Unity PP: ")
                .Append(cameraData.renderPostProcessing ? "ON (!)" : "OFF (custom only)");
        }
        else
        {
            builder.Append(" | URP camera data: MISSING");
        }

        builder.Append("\n\n");
    }

    public static void ConfigureCamera(Camera camera)
    {
        // HDR output belongs only to cameras resolving to a
        // display. Enabling it on an offscreen RenderTexture camera can
        // invalidate that camera's explicitly authored LDR target path.
        if (camera.targetTexture != null)
        {
            return;
        }

        camera.allowHDR = true;
        if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            HDROutputSettings output = HDROutputSettings.main;

            // Only enable HDR on the camera if the user enabled it in settings
            // AND the connected display actually supports and runs HDR.
            cameraData.allowHDROutput = Enabled &&
                output.available && output.active;

            // Fodinae has one post-processing chain: the custom
            // renderer feature. URP FinalBlit still performs the
            // mandatory display color-space conversion and transfer
            // encoding; that output step is not a second PP stack.
            cameraData.renderPostProcessing = false;
        }
    }
}
