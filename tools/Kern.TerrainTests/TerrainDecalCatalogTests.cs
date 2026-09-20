#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainDecalCatalogTests
{
    [TestCase(CellType.Rock, 1)]
    [TestCase(CellType.Boulder2, 1)]
    [TestCase(CellType.WhiteSand, 2)]
    [TestCase(CellType.DarkBlueSand, 2)]
    [TestCase(CellType.Road, 3)]
    [TestCase(CellType.Empty, 4)]
    [TestCase(CellType.BackgroundWithHeavyTraces, 4)]
    [TestCase(CellType.Lava, 0)]
    [TestCase(CellType.XGreen, 0)]
    [TestCase(CellType.Green, 0)]
    [TestCase(CellType.BuildingWall, 0)]
    public void GetFamily_ClassifiesVisualMaterial(
        CellType cellType,
        int expected)
    {
        Assert.That((int)TerrainDecalCatalog.GetFamily(cellType), Is.EqualTo(expected));
    }

    [Test]
    public void GetPackedPlacement_IsDeterministicAndSparse()
    {
        int placed = 0;
        const int sampleSize = 4096;
        for (int i = 0; i < sampleSize; i++)
        {
            int first = TerrainDecalCatalog.GetPackedPlacement(CellType.Rock, i, i * 17);
            int second = TerrainDecalCatalog.GetPackedPlacement(CellType.Rock, i, i * 17);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.InRange(0, 4096));
            placed += first > 0 ? 1 : 0;
        }

        Assert.That(placed, Is.InRange(sampleSize * 26 / 100, sampleSize * 34 / 100));
    }

    [Test]
    public void GetPackedPlacement_ExcludedCellNeverReceivesDecal()
    {
        for (int i = 0; i < 1024; i++)
        {
            Assert.That(
                TerrainDecalCatalog.GetPackedPlacement(CellType.Lava, i, -i),
                Is.Zero);
        }
    }
}
