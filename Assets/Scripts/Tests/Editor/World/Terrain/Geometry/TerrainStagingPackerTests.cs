#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Раскладка изменённых прямоугольников по промежуточным текстурам. Каждая
// лишняя промежуточная текстура в кадре — это синхронизация с render thread
// на каждом из девяти каналов, поэтому число партий и есть цена выгрузки.
[TestFixture]
public sealed class TerrainStagingPackerTests
{
    private const int StagingWidth = 192;
    private const int StagingHeight = 128;

    [Test]
    public void ScatteredSmallRectsShareOneBatch()
    {
        var rects = new List<RectInt>();
        for (int index = 0; index < 13; index++)
        {
            rects.Add(new RectInt((index * 13) % 180, (index * 29) % 300, 1 + (index % 3), 1 + (index % 4)));
        }

        var pieces = new List<TerrainStagedPiece>();
        int batches = TerrainStagingPacker.Pack(rects, StagingWidth, StagingHeight, pieces);

        Assert.That(batches, Is.EqualTo(1));
        AssertValid(rects, pieces, batches);
    }

    [Test]
    public void TallColumnBandIsSplitAndPackedSideBySide()
    {
        // Сдвиг окна на 4 клетки по x: полоса во всю высоту текстуры
        // (две строки на клетку), выше промежуточной.
        var rects = new List<RectInt> { new(10, 0, 4, 320) };
        var pieces = new List<TerrainStagedPiece>();
        int batches = TerrainStagingPacker.Pack(rects, StagingWidth, StagingHeight, pieces);

        Assert.That(pieces.Count, Is.EqualTo(3));
        Assert.That(batches, Is.EqualTo(1));
        AssertValid(rects, pieces, batches);
    }

    [Test]
    public void OverflowOpensAnotherBatch()
    {
        var rects = new List<RectInt> { new(0, 0, StagingWidth, 100), new(0, 100, StagingWidth, 100) };
        var pieces = new List<TerrainStagedPiece>();
        int batches = TerrainStagingPacker.Pack(rects, StagingWidth, StagingHeight, pieces);

        Assert.That(batches, Is.EqualTo(2));
        AssertValid(rects, pieces, batches);
    }

    [Test]
    public void EmptyInputNeedsNoBatch()
    {
        var pieces = new List<TerrainStagedPiece>();
        Assert.That(TerrainStagingPacker.Pack(new List<RectInt>(), StagingWidth, StagingHeight, pieces), Is.Zero);
        Assert.That(pieces, Is.Empty);
    }

    private static void AssertValid(List<RectInt> rects, List<TerrainStagedPiece> pieces, int batches)
    {
        long expectedArea = 0;
        foreach (RectInt rect in rects)
        {
            expectedArea += (long)rect.width * rect.height;
        }

        long packedArea = 0;
        for (int index = 0; index < pieces.Count; index++)
        {
            TerrainStagedPiece piece = pieces[index];
            packedArea += (long)piece.Target.width * piece.Target.height;
            Assert.That(piece.Batch, Is.InRange(0, batches - 1));
            Assert.That(piece.StageX, Is.GreaterThanOrEqualTo(0));
            Assert.That(piece.StageY, Is.GreaterThanOrEqualTo(0));
            Assert.That(piece.StageX + piece.Target.width, Is.LessThanOrEqualTo(StagingWidth));
            Assert.That(piece.StageY + piece.Target.height, Is.LessThanOrEqualTo(StagingHeight));
            if (index > 0)
            {
                Assert.That(piece.Batch, Is.GreaterThanOrEqualTo(pieces[index - 1].Batch), "партии идут подряд");
            }

            for (int other = 0; other < index; other++)
            {
                TerrainStagedPiece previous = pieces[other];
                if (previous.Batch != piece.Batch)
                {
                    continue;
                }

                bool disjoint =
                    piece.StageX >= previous.StageX + previous.Target.width ||
                    previous.StageX >= piece.StageX + piece.Target.width ||
                    piece.StageY >= previous.StageY + previous.Target.height ||
                    previous.StageY >= piece.StageY + piece.Target.height;
                Assert.That(disjoint, Is.True, $"куски {other} и {index} перекрываются в промежуточной текстуре");
            }
        }

        Assert.That(packedArea, Is.EqualTo(expectedArea), "площадь потеряна или задвоена");
    }
}
