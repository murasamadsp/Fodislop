#nullable enable

using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public sealed class LightingRuntimeStateTests
{
    [Test]
    public void OffscreenInvalidationWaitsUntilTheViewportReachesIt()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(200, 0, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.False);
        Assert.That(state.FieldDirty, Is.False);
    }

    [Test]
    public void VisibleInvalidationActivatesExactlyWhenItIntersectsTheViewport()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(24, 24, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.True);
        Assert.That(state.FieldDirty, Is.True);
        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.False);
    }

    [Test]
    public void ActiveInvalidationsKeepTheirIndividualRegions()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        RectInt left = new(4, 4, 8, 8);
        RectInt right = new(40, 12, 6, 10);
        state.QueueRegionInvalidation(left);
        state.QueueRegionInvalidation(right);

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 64, 32)),
            Is.True);
        Assert.That(state.ActiveRegionInvalidations, Has.Count.EqualTo(2));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(left));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(right));
    }

    [Test]
    public void DistantPendingRegionsDoNotFormAFalseBoundingIntersection()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(-100, 0, 8, 8));
        state.QueueRegionInvalidation(new RectInt(100, 0, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(-4, 0, 8, 8)),
            Is.False);
        Assert.That(state.FieldDirty, Is.False);
    }
}
