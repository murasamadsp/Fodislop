#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Прямоугольники изменённой области приходят с сервера, и форма у них
// произвольная. Промежуточная текстура выгрузки при этом постоянного размера,
// поэтому весь вопрос «переживёт ли выгрузка любую форму» сводится к этому
// разрезу. Он обязан покрывать прямоугольник целиком, не выходить за него и
// не посылать ни один тексель дважды — иначе на экране остаётся полоса старых
// данных, и видно это будет не здесь, а через полчаса ходьбы.
[TestFixture]
public sealed class TerrainUploadStripsTests
{
    [Test]
    public void ARectShorterThanTheStagingStripTakesOneStrip()
    {
        var rect = new RectInt(4, 9, 192, 66);

        Assert.That(TerrainUploadStrips.Count(rect.height, 128), Is.EqualTo(1));
        Assert.That(TerrainUploadStrips.At(rect, 128, 0), Is.EqualTo(rect));
    }

    [Test]
    public void AnEmptyRectTakesNoStrips()
    {
        Assert.That(TerrainUploadStrips.Count(0, 128), Is.Zero);
    }

    // Главное свойство. Перебираются все формы до предельных: полоса в один
    // тексель шириной, полоса в один тексель высотой, высота ровно в полоску,
    // на тексель больше и на тексель меньше.
    [Test]
    public void StripsTileAnyRectExactlyOnce()
    {
        int[] stagingRows = [1, 2, 7, 128];
        int[] extents = [1, 2, 3, 127, 128, 129, 255, 256, 320];
        foreach (int rows in stagingRows)
        {
            foreach (int height in extents)
            {
                foreach (int width in extents)
                {
                    var rect = new RectInt(11, 23, width, height);
                    var covered = new HashSet<int>();
                    int count = TerrainUploadStrips.Count(rect.height, rows);
                    for (int index = 0; index < count; index++)
                    {
                        RectInt strip = TerrainUploadStrips.At(rect, rows, index);

                        Assert.That(strip.width, Is.EqualTo(rect.width), "ширина резаться не должна");
                        Assert.That(strip.x, Is.EqualTo(rect.x));
                        Assert.That(strip.height, Is.GreaterThan(0), "пустая полоска");
                        Assert.That(
                            strip.height,
                            Is.LessThanOrEqualTo(rows),
                            "полоска выше промежуточной текстуры");
                        Assert.That(strip.yMin, Is.GreaterThanOrEqualTo(rect.yMin));
                        Assert.That(strip.yMax, Is.LessThanOrEqualTo(rect.yMax));

                        for (int y = strip.yMin; y < strip.yMax; y++)
                        {
                            Assert.That(covered.Add(y), Is.True, $"строка {y} послана дважды");
                        }
                    }

                    Assert.That(
                        covered.Count,
                        Is.EqualTo(rect.height),
                        $"покрыто {covered.Count} строк из {rect.height} при полоске {rows}");
                }
            }
        }
    }
}
