#nullable enable

using System;
using System.Collections.Generic;
using Kern.Rendering;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests;

[TestFixture]
public sealed class LightingQualityTuningTests
{
    [Test]
    public void DefaultStaticQualityFitsStandardLightingWindowBudget()
    {
        LightingQualityTuning quality = LightingConfigHolder.DefaultQuality;
        int width = 192 * quality.FieldPixelsPerCell;
        int height = 128 * quality.FieldPixelsPerCell;
        var cascades = new List<CascadeLayout>();
        CascadeLayoutBuilder.BuildCascadeLayouts(
            width,
            height,
            LightingConfigHolder.CascadeAtlasTextureSize,
            cascades,
            quality.MaximumStaticCascadeDirections,
            quality.FieldPixelsPerCell / quality.CascadeProbePixelsPerCell);

        Assert.That(
            CascadeCostCalculator.EstimateRayWorkUnits(cascades),
            Is.LessThanOrEqualTo(LightingConfigHolder.MaximumStaticCascadeRayWorkUnits));
    }

    [Test]
    public void StaticQualityOutsideRayBudgetIsRejectedBeforeApplying()
    {
        LightingQualityTuning defaults = LightingConfigHolder.DefaultQuality;
        var requested = new LightingQualityTuning(
            defaults.FieldPixelsPerCell,
            defaults.LightPixelsPerCell,
            defaults.CascadeProbePixelsPerCell,
            64,
            defaults.DynamicNearCells,
            defaults.DynamicAngularSampleCount,
            defaults.DynamicEmitterPointsPerAxis,
            defaults.DynamicPolarDirectionCount);
        var settings = new GraphicsQualitySettings(
            lightingPixelsPerCell: 4,
            lightingMaximumTextureDimension: 8192,
            lightingMaximumLightCount: 64,
            lightingCascadeAtlasLimit: 4096,
            renderScale: 1f,
            antiAliasing: 0,
            lightingQuality: LightingQualityMode.PerPixel);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LightingResourceLayout.Validate(192, 128, requested, in settings, new List<CascadeLayout>()))!;
        Assert.That(exception.Message, Does.Contain("единиц работы"));
    }

    [Test]
    public void UnchangedSettingsDoNotAdvanceRevision()
    {
        ulong revision = LightingQualityTuningController.Revision;
        LightingQualityTuningController.Apply(LightingQualityTuningController.Current);
        Assert.That(LightingQualityTuningController.Revision, Is.EqualTo(revision));
    }

    [Test]
    public void ValidSettingsArePublishedAtomically()
    {
        LightingQualityTuning original = LightingQualityTuningController.Current;
        try
        {
            var requested = new LightingQualityTuning(8, 4, 2, 32, 1f, 4, 2, 32);
            if (requested == original)
            {
                requested = new LightingQualityTuning(8, 4, 2, 32, 1f, 8, 2, 32);
            }
            ulong revision = LightingQualityTuningController.Revision;
            LightingQualityTuningController.Apply(requested);
            Assert.That(LightingQualityTuningController.Current, Is.EqualTo(requested));
            Assert.That(LightingQualityTuningController.Revision, Is.EqualTo(revision + 1));
            Assert.That(LightingConfigHolder.AmbientOcclusionPixelsPerCell, Is.EqualTo(32));
        }
        finally
        {
            LightingQualityTuningController.Apply(original);
        }
    }

    [TestCase("field")]
    [TestCase("light")]
    [TestCase("light above field")]
    [TestCase("probe")]
    [TestCase("static angles")]
    [TestCase("near zero")]
    [TestCase("near nan")]
    [TestCase("near infinity")]
    [TestCase("samples")]
    [TestCase("emitter")]
    [TestCase("polar")]
    public void InvalidSettingsPreserveCurrentValuesAndRevision(string input)
    {
        LightingQualityTuning original = LightingQualityTuningController.Current;
        ulong revision = LightingQualityTuningController.Revision;
        LightingQualityTuning invalid = input switch
        {
            "field" => CreateTuning(original, field: 3),
            "light" => CreateTuning(original, light: 3),
            "light above field" => CreateTuning(original, field: 4, light: 8),
            "probe" => CreateTuning(original, probes: 32),
            "static angles" => CreateTuning(original, staticAngles: 128),
            "near zero" => CreateTuning(original, near: 0f),
            "near nan" => CreateTuning(original, near: float.NaN),
            "near infinity" => CreateTuning(original, near: float.PositiveInfinity),
            "samples" => CreateTuning(original, samples: 0),
            "emitter" => CreateTuning(original, emitter: 5),
            "polar" => CreateTuning(original, polar: 63),
            _ => throw new ArgumentException(input),
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingQualityTuningController.Apply(invalid));
        Assert.That(LightingQualityTuningController.Current, Is.EqualTo(original));
        Assert.That(LightingQualityTuningController.Revision, Is.EqualTo(revision));
    }

    [Test]
    public void MaximumAngularQualityIsAcceptedWhenFanCanBeReducedToFit()
    {
        var maximumQuality = new LightingQualityTuning(32, 32, 16, 64, 6f, 64, 4, 1024);
        long required = DynamicPolarWorkBudget.EstimateWorstCaseRayWorkUnits(
            lightWidth: 6144,
            lightHeight: 4096,
            maximumQuality,
            maximumLightCount: 4);

        Assert.That(required, Is.GreaterThan(LightingConfigHolder.MaximumDynamicPolarRayWorkUnits));
        Assert.DoesNotThrow(() =>
            DynamicPolarWorkBudget.ValidateWorstCaseQuality(6144, 4096, maximumQuality, 4));
    }

    [Test]
    public void DynamicPolarWorkBudgetReducesFanWidthInsteadOfExceedingFrameLimit()
    {
        LightingQualityTuning original = LightingQualityTuningController.Current;
        try
        {
            LightingQualityTuningController.Apply(new LightingQualityTuning(
                original.FieldPixelsPerCell,
                original.LightPixelsPerCell,
                original.CascadeProbePixelsPerCell,
                original.MaximumStaticCascadeDirections,
                original.DynamicNearCells,
                original.DynamicAngularSampleCount,
                4,
                1024));

            const int lightCount = 64;
            int rayLength = SystemInfo.maxTextureSize;
            var fans = new int[lightCount];
            var needsTrace = new bool[lightCount];
            var sizes = new Vector2Int[lightCount];
            Array.Fill(fans, 1024);
            Array.Fill(needsTrace, true);
            Array.Fill(sizes, new Vector2Int(1, rayLength));

            int widest = DynamicPolarWorkBudget.AllocateRayFans(
                lightCount, fans, needsTrace, sizes, out int longest);
            long actualWork = (long)widest * LightingComputeBinder.DynamicEmitterPointCount * rayLength * lightCount;

            Assert.That(widest, Is.LessThan(1024));
            Assert.That(longest, Is.EqualTo(rayLength));
            Assert.That(actualWork, Is.LessThanOrEqualTo(LightingConfigHolder.MaximumDynamicPolarRayWorkUnits));
            Assert.That(LightingQualityTuningController.DynamicPolarDirectionCount, Is.EqualTo(1024));
        }
        finally
        {
            LightingQualityTuningController.Apply(original);
        }
    }

    [Test]
    public void DynamicSdfBuildBudgetAcceptsPracticalMapAndRejectsOversizedMap()
    {
        Assert.DoesNotThrow(() => LightingResourceManager.ValidateDynamicDistanceFieldRequest(1024, 1024));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LightingResourceManager.ValidateDynamicDistanceFieldRequest(2048, 1024))!;
        Assert.That(exception.Message, Does.Contain("JFA distance-field build"));
    }

    private static LightingQualityTuning CreateTuning(
        LightingQualityTuning source, int? field = null, int? light = null, int? probes = null,
        int? staticAngles = null, float? near = null, int? samples = null,
        int? emitter = null, int? polar = null) => new(
        field ?? source.FieldPixelsPerCell,
        light ?? source.LightPixelsPerCell,
        probes ?? source.CascadeProbePixelsPerCell,
        staticAngles ?? source.MaximumStaticCascadeDirections,
        near ?? source.DynamicNearCells,
        samples ?? source.DynamicAngularSampleCount,
        emitter ?? source.DynamicEmitterPointsPerAxis,
        polar ?? source.DynamicPolarDirectionCount);
}
