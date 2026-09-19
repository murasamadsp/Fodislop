#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainDecalCatalogTests
{
    [TestCase(CellType.Rock, TerrainDecalFamily.Stone)]
    [TestCase(CellType.Boulder2, TerrainDecalFamily.Stone)]
    [TestCase(CellType.WhiteSand, TerrainDecalFamily.Sand)]
    [TestCase(CellType.DarkBlueSand, TerrainDecalFamily.Sand)]
    [TestCase(CellType.Road, TerrainDecalFamily.Road)]
    [TestCase(CellType.Lava, TerrainDecalFamily.None)]
    [TestCase(CellType.XGreen, TerrainDecalFamily.None)]
    [TestCase(CellType.Green, TerrainDecalFamily.None)]
    [TestCase(CellType.BuildingWall, TerrainDecalFamily.None)]
    public void GetFamily_ClassifiesVisualMaterial(
        CellType cellType,
        TerrainDecalFamily expected)
    {
        Assert.That(TerrainDecalCatalog.GetFamily(cellType), Is.EqualTo(expected));
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
            Assert.That(first, Is.InRange(0, 32));
            placed += first > 0 ? 1 : 0;
        }

        Assert.That(placed, Is.InRange(sampleSize * 18 / 100, sampleSize * 26 / 100));
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
