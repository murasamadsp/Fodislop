#nullable enable

using System.Linq;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainDecalCatalogTests
{
    // Семья — из cells.json. Камень — только красно- и черноскал: атлас
    // нарисован под их гамму.
    [TestCase(CellType.Empty, TerrainDecalFamily.Ground)]
    [TestCase(CellType.RedRock, TerrainDecalFamily.Rock)]
    [TestCase(CellType.BlackRock, TerrainDecalFamily.Rock)]
    [TestCase(CellType.Rock, TerrainDecalFamily.None)]
    [TestCase(CellType.WhiteSand, TerrainDecalFamily.None)]
    [TestCase(CellType.Road, TerrainDecalFamily.None)]
    [TestCase(CellType.Lava, TerrainDecalFamily.None)]
    [TestCase(CellType.BuildingWall, TerrainDecalFamily.None)]
    public void GetFamily_ComesFromCellConfig(CellType cellType, TerrainDecalFamily expected)
    {
        Assert.That(TerrainDecalCatalog.GetFamily(cellType), Is.EqualTo(expected));
    }

    [Test]
    public void GetSurfaceRule_GroundUnderEveryBackgroundAndRockOnRedAndBlackRock()
    {
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.Empty, isBackground: false), Is.EqualTo(TerrainDecalCatalog.GroundRule));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.Rock, isBackground: true), Is.EqualTo(TerrainDecalCatalog.GroundRule));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.RedRock, isBackground: true), Is.EqualTo(TerrainDecalCatalog.GroundRule));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.RedRock, isBackground: false), Is.EqualTo(TerrainDecalCatalog.RockRule));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.BlackRock, isBackground: false), Is.EqualTo(TerrainDecalCatalog.RockRule));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.Rock, isBackground: false), Is.EqualTo(default(TerrainDecalRule)));
        Assert.That(TerrainDecalCatalog.GetSurfaceRule(CellType.Unloaded, isBackground: true), Is.EqualTo(default(TerrainDecalRule)));
    }

    [Test]
    public void Place_IsDeterministicAndSparse()
    {
        int ground = 0;
        int rock = 0;
        const int sampleSize = 4096;
        for (int i = 0; i < sampleSize; i++)
        {
            int first = TerrainDecalCatalog.Place(TerrainDecalCatalog.RockRule, i, i * 17);
            Assert.That(TerrainDecalCatalog.Place(TerrainDecalCatalog.RockRule, i, i * 17), Is.EqualTo(first));
            Assert.That(first == 0 || (first & 4096) != 0, Is.True, "камень — в своём атласе");
            rock += first > 0 ? 1 : 0;
            ground += TerrainDecalCatalog.Place(TerrainDecalCatalog.GroundRule, i, i * 17) > 0 ? 1 : 0;
        }

        Assert.That(rock, Is.InRange(sampleSize * 26 / 100, sampleSize * 34 / 100));
        Assert.That(ground, Is.InRange(sampleSize * 20 / 100, sampleSize * 28 / 100));
    }

    [Test]
    public void Place_EmptyRuleNeverPlaces()
    {
        for (int i = 0; i < 1024; i++)
        {
            Assert.That(TerrainDecalCatalog.Place(default, i, -i), Is.Zero);
        }
    }

    [Test]
    public void Place_GroundUsesAllVariantsAndOffsets()
    {
        bool[] variants = new bool[TerrainDecalCatalog.VariantCount];
        bool[] offsetsX = new bool[4];
        bool[] offsetsY = new bool[4];

        for (int i = 0; i < 20000; i++)
        {
            int packed = TerrainDecalCatalog.Place(TerrainDecalCatalog.GroundRule, i, i * 37);
            if (packed == 0)
            {
                continue;
            }

            int code = packed - 1;
            variants[code & (TerrainDecalCatalog.VariantCount - 1)] = true;
            offsetsX[(code >> 7) & 3] = true;
            offsetsY[(code >> 9) & 3] = true;
        }

        Assert.That(variants.All(value => value), Is.True);
        Assert.That(offsetsX.All(value => value), Is.True);
        Assert.That(offsetsY.All(value => value), Is.True);
    }
}
