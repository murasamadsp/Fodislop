#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering;

internal sealed class UnityHDROutputBackend : HDROutputController.IBackend
{
    public HDROutputController.Snapshot Read()
    {
        try
        {
            return ReadOutput();
        }
        catch (UnityException exception)
        {
            throw new InvalidOperationException("Unable to query the HDR output.", exception);
        }
    }

    private static HDROutputController.Snapshot ReadOutput()
    {
        // Unity throws when HDROutputSettings.main is queried after the
        // display has already switched to SDR. Read the instance availability
        // flag first and avoid touching the other output properties while it
        // is unavailable.
        HDROutputSettings output = HDROutputSettings.main;
        bool available;
        try
        {
            available = output.available;
        }
        catch (UnityException)
        {
            // Unity may invalidate the HDR query for one frame while the
            // display has just switched back to SDR. That is an unavailable
            // output state, so expose it to the controller instead of turning
            // a normal mode change into a terminal read failure.
            available = false;
        }
        DisplayInfo display = Screen.mainWindowDisplayInfo;
        HDRDisplaySupportFlags flags = SystemInfo.hdrDisplaySupportFlags;
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        return new HDROutputController.Snapshot(
            new HDROutputController.OutputIdentity(
                display.name, display.workArea.x, display.workArea.y, display.width, display.height,
                (int)Screen.fullScreenMode),
            (flags & HDRDisplaySupportFlags.Supported) != 0,
            pipeline != null && pipeline.supportsHDR,
            available, available && output.active,
            available && output.HDRModeChangeRequested,
            (flags & HDRDisplaySupportFlags.RuntimeSwitchable) != 0,
            available ? output.paperWhiteNits : 0,
            available ? output.minToneMapLuminance : 0,
            available ? output.maxToneMapLuminance : 0,
            available ? (int)output.displayColorGamut : 0);
    }

    public void Request(bool enabled)
    {
        try
        {
            if (!HDROutputSettings.main.available)
            {
                throw new NotSupportedException("HDR output is unavailable.");
            }

            HDROutputSettings.main.RequestHDRModeChange(enabled);
        }
        catch (UnityException exception)
        {
            throw new InvalidOperationException("Unable to change the HDR output mode.", exception);
        }
    }
}
