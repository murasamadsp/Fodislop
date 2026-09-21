#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using Kern.World.Terrain.Background;
using NUnit.Framework;

namespace Kern.Tests.World;

// Полная сборка клеток идёт через Parallel.For. Значит, её результат обязан
// не зависеть ни от числа потоков, ни от того, какой поток успел первым.
//
// До прогрева метаданных это было не так: FillCell по дороге разрешал тип
// клетки, то есть писал общую структуру и дозаказывал текстуру прямо из
// рабочего потока. Эти тесты — граница, за которую такое не должно вернуться.
[TestFixture]
public sealed class TerrainCellBuilderDeterminismTests
{
    private const int Width = 48;
    private const int Height = 32;
    private const int OriginX = 96;
    private const int OriginY = 64;

    [Test]
    public void BuildFull_RepeatedOnSameWindow_ProducesIdenticalTexels()
    {
        List<TerrainCellTexels> first = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCellTexels> second = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));

        AssertTexelsEqual(first, second, "повторная полная сборка");
    }

    // Последовательная сборка того же окна — независимый оракул: она не
    // касается планировщика вовсе. Расхождение с ней означает гонку.
    [Test]
    public void BuildFull_MatchesSequentialRegionBuild()
    {
        List<TerrainCellTexels> parallel = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCellTexels> sequential = BuildAndSnapshot(builder =>
            builder.BuildRegion(CreateSources(), OriginX, OriginY, 0, 0, Width, Height));

        AssertTexelsEqual(parallel, sequential, "полная против последовательной");
    }

    private static TerrainCellSources CreateSources()
    {
        var world = new TerrainTestWorld();
        return world.BuildSources(
            new TerrainCellCache(),
            new TerrainPrecalculator(),
            new BackgroundFloodFill(),
            OriginX,
            OriginY,
            Width,
            Height);
    }

    private static List<TerrainCellTexels> BuildAndSnapshot(
        System.Action<TerrainCellBuilder> build)
    {
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(Width, Height, 1f);
        build(builder);

        var snapshot = new List<TerrainCellTexels>(
            Width * Height * TerrainCellDataPacker.LayersPerCell);
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                int ringX = TerrainCellDataTextures.Ring(OriginX + x, Width);
                int ringY = TerrainCellDataTextures.Ring(OriginY + y, Height);
                snapshot.Add(builder.Textures.GetCell(
                    ringX, ringY, TerrainCellDataPacker.BackgroundLayer));
                snapshot.Add(builder.Textures.GetCell(
                    ringX, ringY, TerrainCellDataPacker.ForegroundLayer));
            }
        }

        return snapshot;
    }

    private static void AssertTexelsEqual(
        List<TerrainCellTexels> expected,
        List<TerrainCellTexels> actual,
        string what)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count), what);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.That(
                actual[index],
                Is.EqualTo(expected[index]),
                $"{what}: тексель {index} (клетка {index / 2}, слой {index % 2})");
        }
    }
}
