#nullable enable

using System.Collections.Generic;
using System.Linq;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

// Индекс отвечает на один вопрос: приехала текстура типа N — какие клетки
// перечитать. Ответ обязан совпадать с наивной моделью «перебрать все клетки»
// после ЛЮБОЙ последовательности правок.
//
// Обратная сторона индекса лежит плотным массивом по кольцевому адресу окна,
// и это меняет ровно две вещи, которые здесь и проверяются: повторная запись
// того же типа обязана быть неотличима от записи через снятие со старого
// типа, а клетка, уехавшая за край окна, обязана отдать свой слот приехавшей
// на её место — без следа в прямом индексе.
[TestFixture]
public sealed class CellTypeSpatialIndexTests
{
    // Конкретные значения перечисления здесь не важны: индекс не знает, что
    // такое камень, он адресует типы как числа. Берутся два произвольных.
    private const CellType StoneLike = (CellType)5;
    private const CellType SandLike = (CellType)9;

    private const int WindowWidth = 16;
    private const int WindowHeight = 16;

    [Test]
    public void SettingTheSameTypeTwiceKeepsTheCellIndexedOnce()
    {
        CellTypeSpatialIndex index = NewIndex();
        long key = TerrainCoordinateKey.Pack(5, 7);

        index.Set(key, StoneLike);
        index.Set(key, StoneLike);

        Assert.That(KeysOf(index, StoneLike), Is.EquivalentTo(new[] { key }));
    }

    [Test]
    public void ChangingTheTypeMovesTheCellOffThePreviousType()
    {
        CellTypeSpatialIndex index = NewIndex();
        long key = TerrainCoordinateKey.Pack(3, 11);

        index.Set(key, StoneLike);
        index.Set(key, SandLike);

        Assert.That(KeysOf(index, StoneLike), Is.Empty);
        Assert.That(KeysOf(index, SandLike), Is.EquivalentTo(new[] { key }));
    }

    [Test]
    public void UnloadedIsNotIndexedAndClearsThePreviousType()
    {
        CellTypeSpatialIndex index = NewIndex();
        long key = TerrainCoordinateKey.Pack(0, 0);

        index.Set(key, StoneLike);
        index.Set(key, CellType.Unloaded);

        Assert.That(KeysOf(index, StoneLike), Is.Empty);
        Assert.That(KeysOf(index, CellType.Unloaded), Is.Empty);
    }

    // Сдвиг окна: клетка уехала за левый край, на её слот встала приехавшая
    // справа. Прямой индекс обязан отдать только новую — иначе по приходу
    // текстуры террейн полез бы перечитывать клетку, которой в окне нет.
    [Test]
    public void ACellThatScrolledOutReleasesItsSlotToTheOneThatTookItsPlace()
    {
        CellTypeSpatialIndex index = NewIndex();
        long left = TerrainCoordinateKey.Pack(0, 4);
        long enteringRight = TerrainCoordinateKey.Pack(WindowWidth, 4);

        index.Set(left, StoneLike);
        index.Remove(left);
        index.Set(enteringRight, StoneLike);

        Assert.That(KeysOf(index, StoneLike), Is.EquivalentTo(new[] { enteringRight }));
    }

    // Снятие уже уехавшей клетки не должно задевать того, кто занял её слот.
    [Test]
    public void RemovingAStaleCellLeavesTheCurrentOccupantAlone()
    {
        CellTypeSpatialIndex index = NewIndex();
        long stale = TerrainCoordinateKey.Pack(0, 4);
        long current = TerrainCoordinateKey.Pack(WindowWidth, 4);

        index.Set(current, StoneLike);
        index.Remove(stale);

        Assert.That(KeysOf(index, StoneLike), Is.EquivalentTo(new[] { current }));
    }

    // На этом инварианте держится отказ от отдельного прохода «снять уехавшие
    // клетки»: полоса заливки накрывает каждый освободившийся слот, и одна
    // только запись новой клетки обязана убрать прежнюю из прямого индекса.
    [Test]
    public void FillingTheEnteringBandDetachesOutgoingKeysWithoutAnExplicitRemoval()
    {
        CellTypeSpatialIndex index = NewIndex();
        for (int y = 0; y < WindowHeight; y++)
        {
            index.Set(TerrainCoordinateKey.Pack(0, y), StoneLike);
        }

        // Окно уехало на одну клетку вправо: столбец x=0 вышел, x=WindowWidth
        // вошёл и занял те же слоты.
        for (int y = 0; y < WindowHeight; y++)
        {
            index.Set(TerrainCoordinateKey.Pack(WindowWidth, y), SandLike);
        }

        Assert.That(KeysOf(index, StoneLike), Is.Empty, "уехавшие клетки остались в индексе");
        Assert.That(KeysOf(index, SandLike).Length, Is.EqualTo(WindowHeight));
    }

    [Test]
    public void WritingBeforeTheWindowSizeIsKnownThrows()
    {
        var index = new CellTypeSpatialIndex();

        Assert.That(
            () => index.Set(TerrainCoordinateKey.Pack(0, 0), StoneLike),
            Throws.InvalidOperationException);
    }

    [Test]
    public void RandomEditsMatchTheNaiveModel()
    {
        CellTypeSpatialIndex index = NewIndex();
        var model = new Dictionary<long, CellType>();
        var random = new System.Random(20260921);
        CellType[] palette =
        [
            CellType.Unloaded, CellType.Empty, StoneLike, SandLike, CellType.Road,
        ];

        for (int step = 0; step < 20000; step++)
        {
            int x = random.Next(WindowWidth);
            int y = random.Next(WindowHeight);
            long key = TerrainCoordinateKey.Pack(x, y);
            switch (random.Next(6))
            {
                case 0:
                    index.Remove(key);
                    model.Remove(key);
                    break;
                case 1:
                    // Прямоугольник держится внутри окна: за его краем
                    // координата адресует чужой слот, и это ошибка вызывающего,
                    // а не поведение, которое стоит закреплять тестом.
                    int endX = System.Math.Min(WindowWidth, x + random.Next(1, 4));
                    int endY = System.Math.Min(WindowHeight, y + random.Next(1, 4));
                    index.RemoveRect(x, endX, y, endY);
                    foreach (long stale in model.Keys
                        .Where(k => TerrainCoordinateKey.UnpackX(k) >= x &&
                            TerrainCoordinateKey.UnpackX(k) < endX &&
                            TerrainCoordinateKey.UnpackY(k) >= y &&
                            TerrainCoordinateKey.UnpackY(k) < endY)
                        .ToArray())
                    {
                        model.Remove(stale);
                    }

                    break;
                default:
                    CellType type = palette[random.Next(palette.Length)];
                    index.Set(key, type);
                    if (type == CellType.Unloaded)
                    {
                        model.Remove(key);
                    }
                    else
                    {
                        model[key] = type;
                    }

                    break;
            }

            if (step % 97 != 0)
            {
                continue;
            }

            foreach (CellType type in palette)
            {
                long[] expected = model
                    .Where(pair => pair.Value == type)
                    .Select(pair => pair.Key)
                    .OrderBy(k => k)
                    .ToArray();
                Assert.That(
                    KeysOf(index, type).OrderBy(k => k).ToArray(),
                    Is.EqualTo(expected),
                    $"шаг {step}, тип {type}");
            }
        }
    }

    private static long[] KeysOf(CellTypeSpatialIndex index, CellType type)
    {
        var found = new List<(long Key, CellType Type)>();
        index.CollectEntries([type], found);
        return found.Select(entry => entry.Key).ToArray();
    }

    private static CellTypeSpatialIndex NewIndex()
    {
        var index = new CellTypeSpatialIndex();
        index.EnsureWindow(WindowWidth, WindowHeight);
        return index;
    }
}
