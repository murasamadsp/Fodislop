#nullable enable

using Kern.UI;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class WorldMapMipPyramidTests
{
    [Test]
    public void Constructor_RejectsWholeTrailingChunksOutsideWorldBounds()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new WorldMapMipPyramid(
            widthChunks: 2,
            heightChunks: 1,
            chunkSize: 2,
            worldWidth: 2,
            worldHeight: 2,
            cellColorTable: new Color32[256],
            unloadedColor: new Color32(32, 32, 32, 255)));
    }

    [Test]
    public void CompleteStoredScan_OddWorldDimensionsKeepLastChunkInsideEveryMipLevel()
    {
        var cache = new WorldMapMipPyramid(
            widthChunks: 3,
            heightChunks: 5,
            chunkSize: 1,
            worldWidth: 3,
            worldHeight: 5,
            cellColorTable: new Color32[256],
            unloadedColor: new Color32(32, 32, 32, 255));

        for (int chunkIndex = 0; chunkIndex < 15; chunkIndex++)
        {
            cache.AddStoredRun(chunkIndex, CellType.Unloaded, 1);
        }

        cache.CompleteStoredScan();

        Assert.That(cache.MipLevelCount, Is.EqualTo(3));
        Assert.That(cache.GetLevelWidth(0), Is.EqualTo(3));
        Assert.That(cache.GetLevelHeight(0), Is.EqualTo(5));
        Assert.That(cache.GetLevel(2).Length, Is.EqualTo(1));
    }

    [Test]
    public void CompleteStoredScan_UploadsEveryLevelAndPreservesOddEdgePixels()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        colors[2] = new Color32(90, 90, 90, 255);
        colors[3] = new Color32(210, 210, 210, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 3,
            heightChunks: 1,
            chunkSize: 1,
            worldWidth: 3,
            worldHeight: 1,
            cellColorTable: colors,
            unloadedColor: new Color32(32, 32, 32, 255));

        cache.AddStoredRun(0, (CellType)1, 1);
        cache.AddStoredRun(1, (CellType)2, 1);
        cache.AddStoredRun(2, (CellType)3, 1);
        cache.CompleteStoredScan();

        Assert.That(cache.MipLevelCount, Is.EqualTo(2));
        Color32 mipColor = cache.GetLevel(1)[0];
        Assert.That(mipColor.r, Is.EqualTo(137));
        Assert.That(mipColor.g, Is.EqualTo(137));
        Assert.That(mipColor.b, Is.EqualTo(137));
    }

    [Test]
    public void PartialEdgeChunk_IgnoresPaddingAndWeightsItsRealCellsInParentMip()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        colors[2] = new Color32(255, 255, 255, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 2,
            heightChunks: 1,
            chunkSize: 2,
            worldWidth: 3,
            worldHeight: 2,
            cellColorTable: colors,
            unloadedColor: new Color32(32, 32, 32, 255));

        cache.SetChunkCells(0, [(CellType)1, (CellType)1, (CellType)1, (CellType)1]);
        cache.SetChunkCells(1, [(CellType)2, (CellType)2, (CellType)1, (CellType)1]);

        Color32 edgeChunk = cache.GetLevel(0)[1];
        Color32 wholeMap = cache.GetLevel(1)[0];

        Assert.That(edgeChunk.r, Is.EqualTo(255));
        Assert.That(edgeChunk.g, Is.EqualTo(255));
        Assert.That(edgeChunk.b, Is.EqualTo(255));
        Assert.That(wholeMap.r, Is.EqualTo(156).Within(1));
        Assert.That(wholeMap.g, Is.EqualTo(156).Within(1));
        Assert.That(wholeMap.b, Is.EqualTo(156).Within(1));
    }

    [Test]
    public void StoredPartialEdgeChunk_IgnoresOutOfWorldRunCells()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        colors[2] = new Color32(255, 255, 255, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 2,
            heightChunks: 1,
            chunkSize: 2,
            worldWidth: 3,
            worldHeight: 2,
            cellColorTable: colors,
            unloadedColor: new Color32(32, 32, 32, 255));

        cache.AddStoredRun(0, (CellType)1, 4);
        cache.AddStoredRun(1, (CellType)2, 2);
        cache.AddStoredRun(1, (CellType)1, 2);
        cache.CompleteStoredScan();

        Assert.That(cache.GetLevel(0)[1].r, Is.EqualTo(255));
        Assert.That(cache.GetLevel(1)[0].r, Is.EqualTo(156).Within(1));
    }

    [Test]
    public void UnstoredChunks_KeepTheirWeightInTopMipLevels()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 4,
            heightChunks: 1,
            chunkSize: 1,
            worldWidth: 4,
            worldHeight: 1,
            cellColorTable: colors,
            unloadedColor: new Color32(100, 100, 100, 255));

        cache.AddStoredRun(0, (CellType)1, 1);
        cache.CompleteStoredScan();

        Color32 wholeMap = cache.GetLevel(2)[0];
        Assert.That(wholeMap.r, Is.EqualTo(87).Within(1));
    }

    [Test]
    public void UpdatingNPOTBoundaryChunk_RefreshesBothOverlappingMipParents()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        colors[2] = new Color32(255, 255, 255, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 5,
            heightChunks: 1,
            chunkSize: 1,
            worldWidth: 5,
            worldHeight: 1,
            cellColorTable: colors,
            unloadedColor: new Color32(0, 0, 0, 255));

        for (int chunkIndex = 0; chunkIndex < 5; chunkIndex++)
        {
            cache.SetChunkCells(chunkIndex, [(CellType)1]);
        }

        cache.SetChunkCells(2, [(CellType)2]);
        Color32[] mipPixels = cache.GetLevel(1);

        Assert.That(mipPixels[0].r, Is.EqualTo(124).Within(1));
        Assert.That(mipPixels[1].r, Is.EqualTo(124).Within(1));
    }

    [Test]
    public void ChooseBlockSize_PrefersTheFinestBlockThatFitsTheBudget()
    {
        Assert.That(WorldMapMipPyramid.ChooseBlockSize(1024, 1024, 32), Is.EqualTo(1));
        Assert.That(WorldMapMipPyramid.ChooseBlockSize(2048, 2048, 32), Is.EqualTo(2));
        Assert.That(WorldMapMipPyramid.ChooseBlockSize(4096, 4096, 32), Is.EqualTo(4));
        Assert.That(WorldMapMipPyramid.ChooseBlockSize(16384, 16384, 32), Is.EqualTo(16));
    }

    [Test]
    public void ChooseBlockSize_FallsBackToStorageChunkWhenNothingFits()
    {
        Assert.That(WorldMapMipPyramid.ChooseBlockSize(100_000, 100_000, 32), Is.EqualTo(32));
    }

    [Test]
    public void FinerBlock_AveragesCellsInsideTheStorageChunk()
    {
        var colors = new Color32[256];
        colors[1] = new Color32(0, 0, 0, 255);
        colors[2] = new Color32(255, 255, 255, 255);
        var cache = new WorldMapMipPyramid(
            widthChunks: 1,
            heightChunks: 1,
            chunkSize: 2,
            worldWidth: 2,
            worldHeight: 2,
            cellColorTable: colors,
            unloadedColor: new Color32(0, 0, 0, 255),
            blockSize: 1);

        cache.SetChunkCells(0, [(CellType)1, (CellType)1, (CellType)2, (CellType)2]);

        Assert.That(cache.MipBlockSize, Is.EqualTo(1));
        Assert.That(cache.GetLevelWidth(0), Is.EqualTo(2));
        Assert.That(cache.GetLevelHeight(0), Is.EqualTo(2));
        Assert.That(cache.GetLevel(0)[0].r, Is.EqualTo(0));
        Assert.That(cache.GetLevel(0)[1].r, Is.EqualTo(255));
        Assert.That(cache.GetLevel(1)[0].r, Is.EqualTo(187).Within(1));
    }
}
