#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MinimapCellProjectionTests
{
    [TestCase(160, 80, 79)]
    [TestCase(161, 80, 80)]
    public void PlayerCell_MapsToThePixelSelectedByTheRenderProjection(
        int size,
        int expectedX,
        int expectedY)
    {
        Vector2Int pixel = MinimapCellProjection.ServerCellToPixel(30, 40, 30, 40, size);

        Assert.That(pixel, Is.EqualTo(new Vector2Int(expectedX, expectedY)));
        Assert.That(
            MinimapCellProjection.PixelToServerCell(pixel.x, pixel.y, 30, 40, size),
            Is.EqualTo(new Vector2Int(30, 40)));
    }

    [Test]
    public void IncreasingServerY_MovesTowardLowerTextureRows()
    {
        Vector2Int player = MinimapCellProjection.ServerCellToPixel(30, 40, 30, 40, 160);
        Vector2Int below = MinimapCellProjection.ServerCellToPixel(30, 41, 30, 40, 160);

        Assert.That(below.x, Is.EqualTo(player.x));
        Assert.That(below.y, Is.EqualTo(player.y - 1));
        Assert.That(
            MinimapCellProjection.PixelToServerCell(below.x, below.y, 30, 40, 160),
            Is.EqualTo(new Vector2Int(30, 41)));
    }
}
