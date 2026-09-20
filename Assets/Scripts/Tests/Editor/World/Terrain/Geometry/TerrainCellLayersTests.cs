#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellLayersTests
{
    [TestCase(CellType.Empty)]
    [TestCase(CellType.Unloaded)]
    [TestCase(CellType.Rock)]
    public void ExposedGroundHasOneBackgroundQuadAndNoDistortedForeground(CellType propagatedType)
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Empty, propagatedType, true, out CellType background), Is.True);
        Assert.That(background, Is.EqualTo(CellType.Empty));
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Empty, propagatedType, false, out _), Is.False);
    }

    [Test]
    public void SolidBlockKeepsItsForegroundAndUnderlyingGround()
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Empty, false, out CellType foreground), Is.True);
        Assert.That(foreground, Is.EqualTo(CellType.Rock));
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, CellType.Empty, true, out CellType background), Is.True);
        Assert.That(background, Is.EqualTo(CellType.Empty));
    }

    [TestCase(CellType.Unloaded)]
    [TestCase(CellType.Rock)]
    public void MissingOrIdenticalSolidBackgroundIsNotDuplicated(CellType propagatedType)
    {
        Assert.That(TerrainCellLayers.TryGetType(
            CellType.Rock, propagatedType, true, out _), Is.False);
    }
}
