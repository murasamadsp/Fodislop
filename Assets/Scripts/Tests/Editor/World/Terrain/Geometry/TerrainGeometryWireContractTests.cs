#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainGeometryWireContractTests
{
    [TestCase(-2, -2, -2, -2, 1)]
    [TestCase(0, 0, 0, 0, 313)]
    [TestCase(2, 2, 2, 2, 625)]
    public void OrganicEdgeCodeUsesStableBaseFiveOrder(
        int bottom,
        int right,
        int top,
        int left,
        int expected)
    {
        Assert.That(
            TerrainCellGeometry.EncodeOrganicEdges(bottom, right, top, left),
            Is.EqualTo(expected));
    }
}
