#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

[TestFixture]
public class TerrainCellMaskCalculatorTests
{
    [Test]
    public void CalculateTilingDescriptor_WithoutTileGroup_ReturnsZero()
    {
        var empty = new CachedCellData { HasTileGroup = false, TileGroupId = 0 };
        var neighbor = new CachedCellData { HasTileGroup = true, TileGroupId = 1 };

        int descriptor = TerrainCellMaskCalculator.CalculateTilingDescriptor(
            empty, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor);

        Assert.AreEqual(0, descriptor);
    }

    [Test]
    public void CalculateCornerSideMask_OnlyForBuildingWall_FlagsCorners()
    {
        var wall = new CachedCellData { Type = CellType.BuildingWall };
        var floor = new CachedCellData { Type = CellType.Empty };
        var corner = new CachedCellData { Type = CellType.BuildingCorner };
        var regular = new CachedCellData { Type = CellType.Rock };

        int emptyCenter = TerrainCellMaskCalculator.CalculateCornerSideMask(floor, corner, corner, corner, corner);
        Assert.AreEqual(0, emptyCenter);

        int allCorners = TerrainCellMaskCalculator.CalculateCornerSideMask(wall, corner, corner, corner, corner);
        Assert.AreEqual(1 | 2 | 4 | 8, allCorners);

        int leftTop = TerrainCellMaskCalculator.CalculateCornerSideMask(wall, corner, regular, corner, regular);
        Assert.AreEqual(1 | 4, leftTop);
    }

    [Test]
    public void CalculateReliefMask_OnlyEqualNeighbors_SetBitmask()
    {
        var center = new CachedCellData { ReliefGroup = 5 };
        var equal = new CachedCellData { ReliefGroup = 5 };
        var foreign = new CachedCellData { ReliefGroup = 6 };

        byte allEqual = TerrainCellMaskCalculator.CalculateReliefMask(center, equal, equal, equal, equal);
        Assert.AreEqual(1 | 2 | 4 | 8, (int)allEqual);

        byte allForeign = TerrainCellMaskCalculator.CalculateReliefMask(center, foreign, foreign, foreign, foreign);
        Assert.AreEqual(0, (int)allForeign);

        byte topAndRight = TerrainCellMaskCalculator.CalculateReliefMask(center, equal, foreign, foreign, equal);
        Assert.AreEqual(1 | 8, (int)topAndRight);
    }

    [Test]
    public void CalculateSolidBoundaryMask_ImpassableNeighbor_Sets4NeighborBits()
    {
        var shadow = new CachedCellData { Type = CellType.Rock, Properties = CellConfigProperties.None };
        var empty = new CachedCellData { Type = CellType.Empty, Properties = CellConfigProperties.Passable };

        byte allShadow = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            shadow, shadow, shadow, shadow);
        Assert.AreEqual(15, (int)allShadow);

        byte noneShadow = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, empty, empty, empty);
        Assert.AreEqual(0, (int)noneShadow);

        byte topOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            shadow, empty, empty, empty);
        Assert.AreEqual(1, (int)topOnly);

        byte leftOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, shadow, empty, empty);
        Assert.AreEqual(2, (int)leftOnly);

        byte rightOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, empty, empty, shadow);
        Assert.AreEqual(8, (int)rightOnly);
    }
}
