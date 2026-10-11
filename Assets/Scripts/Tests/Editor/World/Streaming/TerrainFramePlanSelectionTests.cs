#nullable enable

using Kern.World.Streaming;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Streaming;

// Опубликованное окно без поставленного света не должно запирать кадр:
// иначе свет и меш показа не ставятся никогда, и экран остаётся чёрным,
// пока запрос окна не станет резидентным.
public sealed class TerrainFramePlanSelectionTests
{
    [Test]
    public void CommittedWindowWithoutLightingYetIsProcessedWhileRequestIsNotResident()
    {
        var calculator = new TerrainViewportCalculator();
        var committed = new StreamingWindow(new Vector2Int(100, 200), new Vector2Int(192, 160));
        var requested = new StreamingWindow(new Vector2Int(100, 200), new Vector2Int(256, 192));
        var camera = new RectInt(150, 240, 60, 40);

        TerrainFramePlan plan = calculator.SelectFramePlan(
            requested,
            committed,
            camera,
            retainedLightingViewport: default,
            isRequestedResident: false,
            hasAnyResidentData: true,
            requestedDimensionsChanged: true,
            cellsCommitted: true,
            cpuBuildInFlight: false,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.True);
        Assert.That(plan.ActiveWindow, Is.EqualTo(committed));
        Assert.That(plan.DimensionsChanged, Is.False);
        Assert.That(plan.LightingViewport, Is.EqualTo(camera));
    }

    [Test]
    public void CameraViewportOutsideCommittedWindowKeepsLightingDemandInsideCommittedBounds()
    {
        var calculator = new TerrainViewportCalculator();
        var committed = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(100, 80));
        var camera = new RectInt(90, -10, 40, 30);

        TerrainFramePlan plan = calculator.SelectFramePlan(
            committed,
            committed,
            camera,
            retainedLightingViewport: default,
            isRequestedResident: true,
            hasAnyResidentData: true,
            requestedDimensionsChanged: false,
            cellsCommitted: true,
            cpuBuildInFlight: true,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.True);
        Assert.That(plan.LightingViewport, Is.EqualTo(new RectInt(60, 0, 40, 30)));
        Assert.That(plan.LightingViewport.xMin, Is.GreaterThanOrEqualTo(committed.Origin.x));
        Assert.That(plan.LightingViewport.yMin, Is.GreaterThanOrEqualTo(committed.Origin.y));
        Assert.That(plan.LightingViewport.xMax, Is.LessThanOrEqualTo(committed.Origin.x + committed.Size.x));
        Assert.That(plan.LightingViewport.yMax, Is.LessThanOrEqualTo(committed.Origin.y + committed.Size.y));
    }

    [Test]
    public void NothingIsProcessedBeforeTheFirstPublication()
    {
        var calculator = new TerrainViewportCalculator();
        var window = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(100, 80));

        TerrainFramePlan plan = calculator.SelectFramePlan(
            window,
            window,
            new RectInt(10, 10, 20, 20),
            retainedLightingViewport: default,
            isRequestedResident: false,
            hasAnyResidentData: false,
            requestedDimensionsChanged: false,
            cellsCommitted: false,
            cpuBuildInFlight: false,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.False);
    }
}
