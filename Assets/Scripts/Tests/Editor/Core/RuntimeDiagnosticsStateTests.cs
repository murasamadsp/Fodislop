#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering.PostProcessing;
using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;

namespace Fodinae.Tests.Core;

public sealed class RuntimeDiagnosticsStateTests
{
    [Test]
    public void RuntimeAssetPaths_UsesPersistentOverrideAndCaseInsensitiveBundledLookup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"fodinae-paths-{Guid.NewGuid():N}");
        string bundled = Path.Combine(root, "bundled");
        string persistent = Path.Combine(root, "persistent");
        Directory.CreateDirectory(Path.Combine(bundled, "Skin"));
        Directory.CreateDirectory(Path.Combine(persistent, "skin"));
        File.WriteAllText(Path.Combine(bundled, "Skin", "Bee.png"), "bundled");
        File.WriteAllText(Path.Combine(persistent, "skin", "bee.png"), "persistent");

        try
        {
            var paths = new RuntimeAssetPaths(bundled, persistent);

            Assert.That(
                paths.FindBundledTextureFile("skin/bee.png"),
                Is.EqualTo(Path.Combine(bundled, "Skin", "Bee.png")));
            Assert.That(
                paths.FindTextureFile("Skin/Bee.png"),
                Is.EqualTo(Path.Combine(persistent, "skin", "bee.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("../secret.png")]
    [TestCase("/absolute.png")]
    [TestCase("skin//bee.png")]
    public void RuntimeAssetPaths_RejectsUnsafeRelativePaths(string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), $"fodinae-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = new RuntimeAssetPaths(root, root);
            Assert.Throws<ArgumentException>(() => paths.FindBundledTextureFile(relativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FrameTelemetry_InstancesDoNotShareMeasurements()
    {
        using var first = new FrameTelemetry();
        using var second = new FrameTelemetry();

        first.TerrainRebuildCount = 3;
        first.LightingBuildCommandsTimeMs = 4.5f;

        Assert.That(second.TerrainRebuildCount, Is.Zero);
        Assert.That(second.LightingBuildCommandsTimeMs, Is.Zero);
    }

    [Test]
    public void ResetFrameTimers_PreservesCumulativeCounters()
    {
        using var telemetry = new FrameTelemetry
        {
            TerrainMeshTimeMs = 2f,
            LightingExecuteCommandsTimeMs = 3f,
            TerrainRebuildCount = 4,
        };

        telemetry.ResetFrameTimers();

        Assert.That(telemetry.TerrainMeshTimeMs, Is.Zero);
        Assert.That(telemetry.LightingExecuteCommandsTimeMs, Is.Zero);
        Assert.That(telemetry.TerrainRebuildCount, Is.EqualTo(4));
    }

    [Test]
    public void RuntimeDebugSettings_DefaultsAreDisabled()
    {
        var settings = new RuntimeDebugSettings();

        Assert.That(settings.IgnoreCollision, Is.False);
        Assert.That(settings.BypassLightingCompute, Is.False);
        Assert.That(settings.BypassTerrainDraw, Is.False);
        Assert.That(settings.BypassCpuMeshRebuild, Is.False);
        Assert.That(settings.ShowRobotDebugVisuals, Is.False);
    }

    [Test]
    public void ColorGradeState_BypassNeutralizesOnlySelectedLayer()
    {
        var state = new ColorGradeState
        {
            Exposure = 1.5f,
            Slope = new Vector3(1.2f, 0.9f, 1.1f),
        };
        state.SetBypassed(ColorGradeLayer.Cdl, bypassed: true);

        ColorGradeSnapshot snapshot = state.ToSnapshot();

        Assert.That(snapshot.Slope, Is.EqualTo(Vector3.one));
        Assert.That(state.EffectiveExposure, Is.EqualTo(1.5f));
        Assert.That(state.Slope, Is.EqualTo(new Vector3(1.2f, 0.9f, 1.1f)));
    }

    [Test]
    public void ColorGradeState_ClearPreviewOverridesPreservesAuthoredValues()
    {
        var state = new ColorGradeState
        {
            Contrast = 0.25f,
            Solo = ColorGradeLayer.Curve,
        };
        state.SetBypassed(ColorGradeLayer.Contrast, bypassed: true);

        state.ClearPreviewOverrides();

        Assert.That(state.HasPreviewOverrides, Is.False);
        Assert.That(state.Contrast, Is.EqualTo(0.25f));
    }

    [Test]
    public void ColorGradeState_AuthoredSnapshotIgnoresPreviewOverrides()
    {
        var state = new ColorGradeState
        {
            Temperature = 30f,
            Solo = ColorGradeLayer.Exposure,
        };
        state.SetBypassed(ColorGradeLayer.WhiteBalance, bypassed: true);

        ColorGradeSnapshot preview = state.ToSnapshot();
        ColorGradeSnapshot authored = state.ToAuthoredSnapshot();

        Assert.That(preview.Temperature, Is.Zero);
        Assert.That(authored.Temperature, Is.EqualTo(30f));
        Assert.That(authored.Transform, Is.EqualTo(state.Transform));
    }

    [Test]
    public void ColorGradeState_SanitizeRepairsNonFiniteAndOutOfRangeValues()
    {
        var state = new ColorGradeState
        {
            Exposure = float.NaN,
            Slope = new Vector3(float.PositiveInfinity, -2f, 8f),
        };

        state.Sanitize();

        Assert.That(state.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(state.Slope.x, Is.EqualTo(PostProcessLook.Grade.Slope.x));
        Assert.That(state.Slope.y, Is.EqualTo(ColorGradeState.SlopeMin));
        Assert.That(state.Slope.z, Is.EqualTo(ColorGradeState.SlopeMax));
    }

    [Test]
    public void ColorGradeZone_WeightIsStableAcrossCoreFeatherAndOutside()
    {
        var zone = new ColorGradeZone(
            "depth",
            centerY: 100f,
            halfHeight: 10f,
            feather: 20f,
            grade: ColorGradeSnapshot.FromLook());

        Assert.That(zone.WeightAt(100f), Is.EqualTo(1f));
        Assert.That(zone.WeightAt(110f), Is.EqualTo(1f));
        Assert.That(zone.WeightAt(120f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(zone.WeightAt(130f), Is.Zero);
        Assert.That(zone.WeightAt(float.NaN), Is.Zero);
    }

    [Test]
    public void ColorGradeZone_DefaultsKeepTheAuthoredBaseLook()
    {
        var zone = new ColorGradeZone(
            "depth",
            centerY: 100f,
            halfHeight: 10f,
            feather: 20f,
            grade: ColorGradeSnapshot.FromLook());

        Assert.That(zone.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(zone.Contrast, Is.EqualTo(PostProcessLook.ColorGrading.Contrast));
        Assert.That(zone.Saturation, Is.EqualTo(PostProcessLook.ColorGrading.Saturation));
    }

    [Test]
    public void ColorGradeZoneDriver_DisabledZonesRestoreBaseGrade()
    {
        ColorGradeSnapshot modified = ColorGradeSnapshot.FromLook().WithTemperature(40f);
        PostProcessRuntimeState.SetColorGrade(modified);
        var zones = new ColorGradeZones
        {
            Enabled = false,
        };

        ColorGradeZones.Resolution resolution = ColorGradeZoneDriver.Push(zones, camera: null);

        Assert.That(
            PostProcessRuntimeState.ColorGrade,
            Is.EqualTo(ColorGradeSnapshot.FromLook()));
        Assert.That(
            resolution.Exposure,
            Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(
            resolution.Contrast,
            Is.EqualTo(PostProcessLook.ColorGrading.Contrast));
        Assert.That(
            resolution.Saturation,
            Is.EqualTo(PostProcessLook.ColorGrading.Saturation));
    }

    [Test]
    public void ColorGradeZones_ResolveIncludesTheFullAuthoredLook()
    {
        var zones = new ColorGradeZones
        {
            Enabled = true,
        };
        zones.Add(new ColorGradeZone(
            "depth",
            centerY: 20f,
            halfHeight: 5f,
            feather: 5f,
            grade: ColorGradeSnapshot.FromLook().WithTemperature(25f),
            exposure: 1.25f,
            contrast: 0.2f,
            saturation: 0.7f));

        ColorGradeZones.Resolution result = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            20f);

        Assert.That(result.Exposure, Is.EqualTo(1.25f));
        Assert.That(result.Contrast, Is.EqualTo(0.2f));
        Assert.That(result.Saturation, Is.EqualTo(0.7f));
        Assert.That(result.Grade.Temperature, Is.EqualTo(25f));
    }

    [Test]
    public void ColorGradeZone_2DBounds_RestrictsWeightOutsideWidth()
    {
        var zone = new ColorGradeZone(
            "room",
            centerY: 50f,
            halfHeight: 10f,
            feather: 5f,
            grade: ColorGradeSnapshot.FromLook(),
            centerX: 100f,
            halfWidth: 20f);

        // Center inside
        Assert.That(zone.WeightAt(100f, 50f), Is.EqualTo(1f));
        // Along X inside width
        Assert.That(zone.WeightAt(115f, 50f), Is.EqualTo(1f));
        // Outside X feather
        Assert.That(zone.WeightAt(130f, 50f), Is.Zero);
        // Inside X but outside Y
        Assert.That(zone.WeightAt(100f, 70f), Is.Zero);
    }

    [Test]
    public void ColorGradeZones_2DResolve_AppliesZoneWithinBounds()
    {
        var zones = new ColorGradeZones
        {
            Enabled = true,
        };
        zones.Add(new ColorGradeZone(
            "cave",
            centerY: 50f,
            halfHeight: 10f,
            feather: 5f,
            grade: ColorGradeSnapshot.FromLook().WithTemperature(-30f),
            exposure: -1.0f,
            centerX: 200f,
            halfWidth: 50f));

        // Inside 2D bounds
        ColorGradeZones.Resolution inside = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            200f,
            50f);
        Assert.That(inside.Exposure, Is.EqualTo(-1.0f));
        Assert.That(inside.Grade.Temperature, Is.EqualTo(-30f));

        // Outside X bounds
        ColorGradeZones.Resolution outside = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            300f,
            50f);
        Assert.That(outside.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
    }

    [Test]
    public void PostProcessRuntimeState_DisplayCalibrationRepairsNonFiniteValues()
    {
        PostProcessRuntimeState.SetDisplayCalibration(
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity);

        Assert.That(
            PostProcessRuntimeState.DisplayGamma,
            Is.EqualTo(DisplaySettings.DefaultGamma));
        Assert.That(
            PostProcessRuntimeState.DisplayPaperWhiteNits,
            Is.EqualTo(DisplaySettings.DefaultPaperWhite));
        Assert.That(
            PostProcessRuntimeState.DisplayPeakBrightnessNits,
            Is.EqualTo(DisplaySettings.DefaultPeakBrightness));
    }

    [Test]
    public void PostProcessRuntimeState_DisplayPeakNeverFallsBelowPaperWhite()
    {
        PostProcessRuntimeState.SetDisplayCalibration(
            DisplaySettings.DefaultGamma,
            DisplaySettings.PaperWhiteMax,
            DisplaySettings.PeakBrightnessMin);

        Assert.That(
            PostProcessRuntimeState.DisplayPeakBrightnessNits,
            Is.EqualTo(DisplaySettings.PaperWhiteMax));
    }
}
