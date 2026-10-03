#nullable enable

using Kern.UI;
using NUnit.Framework;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapViewportChunkBudgetTests
{
    [Test]
    public void RequiresMip_WhenVisibleGpuGridExceedsPackedSlotLimitBelowScaleThreshold()
    {
        bool requiresMip = MapViewportChunkBudget.ShouldUseMip(
            worldWidth: 100_000,
            worldHeight: 100_000,
            texWidth: 1024,
            texHeight: 768,
            cellsPerPixel: 31f,
            viewCenterX: 50_000f,
            viewCenterY: 50_000f);

        Assert.That(requiresMip, Is.True);
    }

    [Test]
    public void DoesNotRequireMip_WhenVisibleGpuGridFitsPackedSlotLimit()
    {
        bool requiresMip = MapViewportChunkBudget.ShouldUseMip(
            worldWidth: 100_000,
            worldHeight: 100_000,
            texWidth: 1024,
            texHeight: 768,
            cellsPerPixel: 1f,
            viewCenterX: 50_000f,
            viewCenterY: 50_000f);

        Assert.That(requiresMip, Is.False);
    }

    [Test]
    public void RequiresMip_ClampsVisibleGridToWorldEdges()
    {
        bool requiresMip = MapViewportChunkBudget.ShouldUseMip(
            worldWidth: 1024,
            worldHeight: 1024,
            texWidth: 1024,
            texHeight: 1024,
            cellsPerPixel: 2f,
            viewCenterX: 512f,
            viewCenterY: 512f);

        Assert.That(requiresMip, Is.False);
    }

    [Test]
    public void DoesNotRequireMip_When1080pViewportAtNormalScale()
    {
        bool requiresMip = MapViewportChunkBudget.ShouldUseMip(
            worldWidth: 100_000,
            worldHeight: 100_000,
            texWidth: 1920,
            texHeight: 1080,
            cellsPerPixel: 1f,
            viewCenterX: 50_000f,
            viewCenterY: 50_000f);

        Assert.That(requiresMip, Is.False);
    }

    [Test]
    public void RequiresMip_WhenVisibleGridExceedsBudgetEvenBelowTheOldScaleThreshold()
    {
        // Regression: the old scale gate (cellsPerPixel < 8) returned false before
        // the area check, so the mip path was skipped exactly where the grid no
        // longer fit and the map degenerated to "unknown" stripes.
        bool requiresMip = MapViewportChunkBudget.ShouldUseMip(
            worldWidth: 100_000,
            worldHeight: 100_000,
            texWidth: 960,
            texHeight: 540,
            cellsPerPixel: 4f,
            viewCenterX: 50_000f,
            viewCenterY: 50_000f);

        Assert.That(requiresMip, Is.True);
    }
}
