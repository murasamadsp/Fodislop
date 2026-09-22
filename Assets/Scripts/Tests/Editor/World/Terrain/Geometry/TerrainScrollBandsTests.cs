#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Полосы сдвига — единственное место, где террейн решает, что пересчитать
// после шага камеры. Два свойства обязаны держаться на любых входах: ничего не
// пропущено и ничего не посчитано дважды. Второе — не про экономию: заливка
// фона на повторной клетке даёт другой результат, а не лишнюю работу.
[TestFixture]
public sealed class TerrainScrollBandsTests
{
    [Test]
    public void Resolve_NoMovement_ProducesNoBands()
    {
        TerrainScrollBands bands = TerrainScrollBands.Resolve(32, 24, 0, 0);

        Assert.That(bands.ColumnBand.width * bands.ColumnBand.height, Is.Zero);
        Assert.That(bands.RowBand.width * bands.RowBand.height, Is.Zero);
    }

    [Test]
    public void Resolve_Fuzz_CoversEveryEnteredCellExactlyOnce(
        [Values(1, 7, 16, 33)] int width,
        [Values(1, 5, 24)] int height,
        [Values(-9, -3, -1, 0, 1, 4, 12)] int dx,
        [Values(-7, -2, 0, 1, 6)] int dy,
        [Values(0, 1)] int margin)
    {
        TerrainScrollBands bands = TerrainScrollBands.Resolve(width, height, dx, dy, margin);

        var covered = new int[width, height];
        Mark(covered, bands.ColumnBand, width, height);
        Mark(covered, bands.RowBand, width, height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Assert.That(
                    covered[x, y],
                    Is.LessThanOrEqualTo(1),
                    $"клетка {x},{y} попала в обе полосы");

                // Клетка после сдвига держит данные старой (x+dx, y+dy).
                // Вышла за окно — значит, данных нет и её обязаны пересчитать.
                bool entered = (uint)(x + dx) >= (uint)width || (uint)(y + dy) >= (uint)height;
                if (entered)
                {
                    Assert.That(covered[x, y], Is.EqualTo(1), $"клетка {x},{y} не пересчитана");
                }
            }
        }
    }

    // Кайма расширяет полосу внутрь окна: у клетки на старой границе сосед
    // снаружи только что появился, и её маска соседства больше не верна.
    [Test]
    public void Resolve_WithNeighbourMargin_ExtendsBandOneCellInwards(
        [Values(-4, -1, 1, 5)] int delta)
    {
        const int Size = 32;
        TerrainScrollBands bare = TerrainScrollBands.Resolve(Size, Size, delta, 0);
        TerrainScrollBands padded = TerrainScrollBands.Resolve(Size, Size, delta, 0, neighbourMargin: 1);

        Assert.That(padded.ColumnBand.width, Is.EqualTo(bare.ColumnBand.width + 1));
        if (delta > 0)
        {
            Assert.That(padded.ColumnBand.xMin, Is.EqualTo(bare.ColumnBand.xMin - 1));
            Assert.That(padded.ColumnBand.xMax, Is.EqualTo(Size));
        }
        else
        {
            Assert.That(padded.ColumnBand.xMin, Is.Zero);
        }
    }

    // Полоса не имеет права вылезти за окно: индексы идут прямо в массивы.
    [Test]
    public void Resolve_DeltaLargerThanWindow_StaysInsideBounds(
        [Values(-40, -33, 33, 40)] int delta)
    {
        const int Size = 32;
        TerrainScrollBands bands = TerrainScrollBands.Resolve(Size, Size, delta, delta, neighbourMargin: 1);

        AssertInside(bands.ColumnBand, Size, Size);
        AssertInside(bands.RowBand, Size, Size);
    }

    private static void AssertInside(RectInt band, int width, int height)
    {
        if (band.width == 0 || band.height == 0)
        {
            return;
        }

        Assert.That(band.xMin, Is.GreaterThanOrEqualTo(0));
        Assert.That(band.yMin, Is.GreaterThanOrEqualTo(0));
        Assert.That(band.xMax, Is.LessThanOrEqualTo(width));
        Assert.That(band.yMax, Is.LessThanOrEqualTo(height));
    }

    private static void Mark(int[,] covered, RectInt band, int width, int height)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            for (int y = band.yMin; y < band.yMax; y++)
            {
                Assert.That((uint)x, Is.LessThan((uint)width));
                Assert.That((uint)y, Is.LessThan((uint)height));
                covered[x, y]++;
            }
        }
    }
}
