#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainGeometryWireContractTests
{
    [Test]
    public void CornerOrderIsCounterClockwiseFromBottomLeft()
    {
        var geometry = TerrainCellGeometry.FromOffsets(
            UnityEngine.Vector3.zero, UnityEngine.Vector3.zero,
            UnityEngine.Vector3.zero, UnityEngine.Vector3.zero);
        Assert.That(geometry.GetCorner(0), Is.EqualTo(new UnityEngine.Vector2(0f, 0f)));
        Assert.That(geometry.GetCorner(1), Is.EqualTo(new UnityEngine.Vector2(1f, 0f)));
        Assert.That(geometry.GetCorner(2), Is.EqualTo(new UnityEngine.Vector2(1f, 1f)));
        Assert.That(geometry.GetCorner(3), Is.EqualTo(new UnityEngine.Vector2(0f, 1f)));
        Assert.That(geometry.IsAnchored, Is.False);
    }
}
