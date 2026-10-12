#nullable enable

using Kern.UI;
using NUnit.Framework;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapChunkGridTests
{
    [Test]
    public void Compute_ClampsVisibleGridToWorldBounds()
    {
        MapChunkGrid grid = MapChunkGrid.Compute(
            worldWidth: 1024,
            worldHeight: 1024,
            texWidth: 1024,
            texHeight: 1024,
            cellsPerPixel: 2f,
            viewCenterX: 512f,
            viewCenterY: 512f);

        Assert.That(grid.MinChunkX, Is.EqualTo(0));
        Assert.That(grid.MinChunkY, Is.EqualTo(0));
        Assert.That(grid.Width, Is.EqualTo(64));
        Assert.That(grid.Height, Is.EqualTo(64));
        Assert.That(grid.Area, Is.EqualTo(4096));
    }

    [Test]
    public void Compute_ReportsAreaLargeEnoughToRequireMip()
    {
        MapChunkGrid grid = MapChunkGrid.Compute(
            worldWidth: 100_000,
            worldHeight: 100_000,
            texWidth: 960,
            texHeight: 540,
            cellsPerPixel: 4f,
            viewCenterX: 50_000f,
            viewCenterY: 50_000f);

        Assert.That(grid.Area, Is.GreaterThan(MapViewportChunkBudget.MaxPackedChunkSlots));
    }

    [Test]
    public void Compute_InvalidViewReturnsEmptyGrid()
    {
        Assert.That(MapChunkGrid.Compute(0, 0, 0, 0, 0f, 0f, 0f).Area, Is.EqualTo(0));
    }

    [Test]
    public void ShouldUseMip_AgreesWithGridAreaForEveryScale()
    {
        for (float cellsPerPixel = 1f; cellsPerPixel <= 16f; cellsPerPixel += 0.5f)
        {
            bool expected = MapChunkGrid.Compute(
                    100_000,
                    100_000,
                    960,
                    540,
                    cellsPerPixel,
                    50_000f,
                    50_000f).Area > MapViewportChunkBudget.MaxPackedChunkSlots;
            bool actual = MapViewportChunkBudget.ShouldUseMip(
                100_000,
                100_000,
                960,
                540,
                cellsPerPixel,
                50_000f,
                50_000f);

            Assert.That(actual, Is.EqualTo(expected), $"cellsPerPixel={cellsPerPixel}");
        }
    }
}
