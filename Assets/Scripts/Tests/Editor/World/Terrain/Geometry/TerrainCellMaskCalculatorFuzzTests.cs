#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class TerrainCellMaskCalculatorFuzzTests
{
    [TestCase(0, 0)]
    [TestCase(7, 15)]
    [TestCase(0, 7)]
    [TestCase(255, 255)]
    [TestCase(0, 255)]
    [TestCase(128, 128)]
    public void CalculateReliefMask_AlwaysFitsInFourBits(int data, int neighbor)
    {
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(
            new CachedCellData { ReliefGroup = (byte)data },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor });
        Assert.That(mask, Is.InRange(0, 15), $"data={data}, neighbor={neighbor}");
    }

    [Test]
    public void CalculateReliefMask_SameGroup_ProducesFullMask()
    {
        var c = new CachedCellData { ReliefGroup = 5 };
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(c, c, c, c, c);
        Assert.That(mask, Is.EqualTo(15));
    }

    // Бит ставится только на равенстве: рельефная группа — семья, а не
    // высота. Раньше сравнение было порядковым, и шов между двумя семьями
    // рисовался лишь с той стороны, где номер больше.
    [Test]
    public void CalculateReliefMask_ForeignNeighbors_ProduceZero()
    {
        var center = new CachedCellData { ReliefGroup = 5 };
        var higher = new CachedCellData { ReliefGroup = 7 };
        var lower = new CachedCellData { ReliefGroup = 3 };
        Assert.That(
            TerrainCellMaskCalculator.CalculateReliefMask(center, higher, higher, higher, higher),
            Is.EqualTo(0));
        Assert.That(
            TerrainCellMaskCalculator.CalculateReliefMask(center, lower, lower, lower, lower),
            Is.EqualTo(0));
    }

    // Обе стороны шва обязаны видеть друг друга чужими, иначе кайму рисует
    // одна клетка из двух и граница выглядит смещённой на полклетки.
    [Test]
    public void CalculateReliefMask_ForeignPairIsSymmetric()
    {
        var crystal = new CachedCellData { ReliefGroup = 3 };
        var rock = new CachedCellData { ReliefGroup = 5 };
        byte fromCrystal = TerrainCellMaskCalculator.CalculateReliefMask(
            crystal, rock, crystal, crystal, crystal);
        byte fromRock = TerrainCellMaskCalculator.CalculateReliefMask(
            rock, rock, rock, crystal, rock);
        Assert.That(fromCrystal & 1, Is.EqualTo(0), "кристалл не считает породу своей");
        Assert.That(fromRock & 4, Is.EqualTo(0), "порода не считает кристалл своим");
    }

    // Клетка без рельефа не обводится ничем: у неё нет семьи, и кайма по
    // всем четырём сторонам залила бы пол сеткой.
    [Test]
    public void CalculateReliefMask_NoReliefGroup_ProducesZero()
    {
        var ground = new CachedCellData { ReliefGroup = 0 };
        Assert.That(
            TerrainCellMaskCalculator.CalculateReliefMask(ground, ground, ground, ground, ground),
            Is.EqualTo(0));
    }

    [Test]
    public void CalculateCornerSideMask_NonWallCell_AlwaysZero()
    {
        var data = new CachedCellData { Type = CellType.Rock };
        int mask = TerrainCellMaskCalculator.CalculateCornerSideMask(
            data, new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData());
        Assert.That(mask, Is.EqualTo(0));
    }

    [Test]
    public void CalculateSolidBoundaryMask_AlwaysFitsInEightBits()
    {
        byte mask = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData());
        Assert.That(mask, Is.InRange(0, 255));
    }

    [Test]
    public void CalculateTilingDescriptor_AlwaysValidTileIndex()
    {
        int desc = TerrainCellMaskCalculator.CalculateTilingDescriptor(
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData());
        int baseIndex = desc & 0x1F;
        Assert.That(baseIndex, Is.LessThanOrEqualTo(13));
    }
}
