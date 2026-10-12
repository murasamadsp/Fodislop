#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapProjectionTests
{
    [TestCase(0f, 0f, 0.25f)]
    [TestCase(480.5f, 269.5f, 1f)]
    [TestCase(959f, 539f, 8f)]
    public void MapPixelToServer_UsesServerYDown(float pixelX, float pixelY, float scale)
    {
        Vector2 actual = MapProjection.MapPixelToServer(
            pixelX,
            pixelY,
            1200f,
            700f,
            scale,
            960,
            540);

        Assert.That(actual.x, Is.EqualTo(1200f + (pixelX - 480f) * scale).Within(0.001f));
        Assert.That(actual.y, Is.EqualTo(700f + (pixelY - 270f) * scale).Within(0.001f));
    }

    [Test]
    public void MipSelection_UsesChunkBufferBudgetBeforeScaleThreshold()
    {
        Assert.That(
            MapViewportChunkBudget.ShouldUseMip(
                worldWidth: 100_000,
                worldHeight: 100_000,
                texWidth: 1024,
                texHeight: 768,
                cellsPerPixel: 31f,
                viewCenterX: 50_000f,
                viewCenterY: 50_000f),
            Is.True);
        Assert.That(
            MapViewportChunkBudget.ShouldUseMip(
                worldWidth: 100_000,
                worldHeight: 100_000,
                texWidth: 1024,
                texHeight: 768,
                cellsPerPixel: 1f,
                viewCenterX: 50_000f,
                viewCenterY: 50_000f),
            Is.False);
    }

    [Test]
    public void ServerCellToTexturePixel_MapsServerDownToTextureUp()
    {
        Vector2 actual = MapProjection.ServerCellToTexturePixel(
            101f,
            205f,
            100f,
            200f,
            1f,
            960,
            540);

        Assert.That(actual.x, Is.EqualTo(481f));
        Assert.That(actual.y, Is.EqualTo(264f));
    }

    [Test]
    public void MinimapProjection_MatchesEvenSizedTextureSamplingAndServerYDown()
    {
        Vector2Int centerPixel = MapProjection.ServerCellToMinimapPixel(30, 40, 30, 40, 160);
        Vector2Int belowPlayer = MapProjection.ServerCellToMinimapPixel(30, 41, 30, 40, 160);
        Vector2Int centerCell = MapProjection.MinimapPixelToServerCell(80, 79, 30, 40, 160);
        Vector2Int belowWorldSample = MapProjection.MinimapPixelToServerCell(80, 78, 30, 40, 160);

        Assert.That(centerPixel, Is.EqualTo(new Vector2Int(80, 79)));
        Assert.That(belowPlayer, Is.EqualTo(new Vector2Int(80, 78)));
        Assert.That(centerCell, Is.EqualTo(new Vector2Int(30, 40)));
        Assert.That(belowWorldSample, Is.EqualTo(new Vector2Int(30, 41)));
    }

    [Test]
    public void UnknownCellColor_IsDeterministicAndStriped()
    {
        Color32 first = MapProjection.UnknownCellColor(4, 6);
        Color32 repeated = MapProjection.UnknownCellColor(4, 6);
        Color32 nextStripe = MapProjection.UnknownCellColor(6, 6);

        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(nextStripe, Is.Not.EqualTo(first));
    }

    [Test]
    public void SampleCellColor_DistinguishesUnknownFromOutOfBounds()
    {
        var sampler = new MapCellSampler();
        var colors = new Color32[256];

        Color32 unknown = MapProjection.SampleCellColor(
            sampler,
            colors,
            2,
            2,
            10,
            10,
            Color.black,
            out bool unknownWasLoaded);
        Color32 outOfBounds = MapProjection.SampleCellColor(
            sampler,
            colors,
            -1,
            2,
            10,
            10,
            Color.black,
            out bool outOfBoundsWasLoaded);

        Assert.That(unknown, Is.EqualTo(MapProjection.UnknownCellColor(2, 2)));
        Assert.That(unknownWasLoaded, Is.False);
        Assert.That(outOfBounds, Is.EqualTo((Color32)Color.black));
        Assert.That(outOfBoundsWasLoaded, Is.False);
    }

    [Test]
    public void ClampCenter_AllowsPanningIntoNegativeCoordinatesAndBorders()
    {
        // Viewport half-width is 480, world width is 1000
        float clampedNegative = MapViewportBounds.ClampCenter(-100f, 480f, 1000);
        float clampedZero = MapViewportBounds.ClampCenter(0f, 480f, 1000);
        float clampedFarRight = MapViewportBounds.ClampCenter(1200f, 480f, 1000);

        Assert.That(clampedNegative, Is.EqualTo(-100f));
        Assert.That(clampedZero, Is.EqualTo(0f));
        Assert.That(clampedFarRight, Is.EqualTo(1200f));
    }

    [Test]
    public void ClampCenter_WhenViewportLargerThanWorld_DoesNotForceWorldCenter()
    {
        // Zoomed out: half-width 600 > world width 500 / 2
        float clamped = MapViewportBounds.ClampCenter(100f, 600f, 500);

        Assert.That(clamped, Is.EqualTo(100f));
    }

    [Test]
    public void ComputeMaxZoomOut_AllowsReasonableZoomOnSmallAndLargeWorlds()
    {
        float smallWorldZoom = MapViewportBounds.ComputeMaxZoomOut(960, 540, 100, 100);
        float largeWorldZoom = MapViewportBounds.ComputeMaxZoomOut(960, 540, 5000, 5000);

        Assert.That(smallWorldZoom, Is.GreaterThanOrEqualTo(4f));
        Assert.That(largeWorldZoom, Is.GreaterThanOrEqualTo(4f));
    }

    [Test]
    public void ViewportWorldY_RowZeroIsBottomAndTopRowIsSurface()
    {
        float cy = 200f;
        int texH = 540;
        float cp = 1f;

        float startWorldY = cy + (texH * 0.5f - 0.5f) * cp;
        float bottomRowY = startWorldY - (0f * cp);
        float topRowY = startWorldY - ((texH - 1) * cp);

        // In Server Y, larger values are underground (bottom of world).
        // Row 0 of texture is the bottom of the UI element.
        Assert.That(bottomRowY, Is.GreaterThan(cy), "Row 0 of texture must be deeper underground (bottom of view)");
        Assert.That(topRowY, Is.LessThan(cy), "Top row of texture must be towards surface (top of view)");
        Assert.That(bottomRowY, Is.EqualTo(200f + 269.5f).Within(0.001f));
        Assert.That(topRowY, Is.EqualTo(200f - 269.5f).Within(0.001f));
    }
}
