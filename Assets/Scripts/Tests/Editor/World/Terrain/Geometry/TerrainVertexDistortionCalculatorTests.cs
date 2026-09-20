#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class TerrainVertexDistortionCalculatorTests
{
    [Test]
    public void EnsureCapacity_AllocatesCorrectGridDimensions()
    {
        var calculator = new TerrainVertexDistortionCalculator();
        calculator.EnsureCapacity(10, 20);

        Assert.IsNotNull(calculator.GridVertexOffsets);
        Assert.AreEqual(11, calculator.GridVertexOffsets.GetLength(0));
        Assert.AreEqual(21, calculator.GridVertexOffsets.GetLength(1));
    }

    [Test]
    public void ComputeOffset_WorldBounds_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };

        TerrainVertexOffset minX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 0, 10, 100, 100);
        TerrainVertexOffset maxX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 100, 10, 100, 100);
        TerrainVertexOffset minY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 0, 100, 100);
        TerrainVertexOffset maxY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 100, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, minX);
        Assert.AreEqual(TerrainVertexOffset.Zero, maxX);
        Assert.AreEqual(TerrainVertexOffset.Zero, minY);
        Assert.AreEqual(TerrainVertexOffset.Zero, maxY);
    }

    [Test]
    public void ComputeOffset_WhenAnyNeighborIsBlock_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var block = new CachedCellData { Distortion = CellDistortionType.Block };

        TerrainVertexOffset tlBlock = TerrainVertexDistortionCalculator.ComputeOffset(block, cause, cause, cause, 10, 10, 100, 100);
        TerrainVertexOffset trBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, block, cause, cause, 10, 10, 100, 100);
        TerrainVertexOffset blBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, block, cause, 10, 10, 100, 100);
        TerrainVertexOffset brBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, block, 10, 10, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, tlBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, trBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, blBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, brBlock);
    }

    [Test]
    public void ComputeOffset_AllFourAreCause_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        int worldX = 15;
        int worldY = 25;

        // Upstream (15bced90): четыре источника вокруг — вершина не сдвигается.
        TerrainVertexOffset result = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, worldX, worldY, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, result);
    }

    [Test]
    public void ComputeOffset_TwoOppositeAreCause_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var none = new CachedCellData { Distortion = (CellDistortionType)0 };

        TerrainVertexOffset diagonal1 = TerrainVertexDistortionCalculator.ComputeOffset(cause, none, none, cause, 10, 10, 100, 100);
        TerrainVertexOffset diagonal2 = TerrainVertexDistortionCalculator.ComputeOffset(none, cause, cause, none, 10, 10, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, diagonal1);
        Assert.AreEqual(TerrainVertexOffset.Zero, diagonal2);
    }

    [Test]
    public void ComputeOffset_TopAdjacentCause_PushesDown()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var none = new CachedCellData { Distortion = (CellDistortionType)0 };
        int worldX = 12;
        int worldY = 18;

        int expectedRy = (int)TerrainVertexDistortionCalculator.RandYd(worldX, worldY);
        var expected = new TerrainVertexOffset(0, -expectedRy, 0);

        TerrainVertexOffset result = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, none, none, worldX, worldY, 100, 100);

        Assert.AreEqual(expected, result);
    }

    [Test]
    public void RandMath_StaysWithinSeven()
    {
        for (int x = 1; x <= 50; x++)
        {
            for (int y = 1; y <= 50; y++)
            {
                float rx = TerrainVertexDistortionCalculator.RandXd(x, y);
                float ry = TerrainVertexDistortionCalculator.RandYd(x, y);

                Assert.IsTrue(rx >= 0 && rx < 7);
                Assert.IsTrue(ry >= 0 && ry < 7);
            }
        }
    }

    [Test]
    public void TerrainVertexOffset_ConvertsStepsToWorldOffset()
    {
        Vector3 result = new TerrainVertexOffset(1, -3, 6).ToVector3();

        Assert.That(result.x, Is.EqualTo(1f / 32f).Within(0.000001f));
        Assert.That(result.y, Is.EqualTo(-3f / 32f).Within(0.000001f));
        Assert.That(result.z, Is.EqualTo(6f / 32f).Within(0.000001f));
    }
}
