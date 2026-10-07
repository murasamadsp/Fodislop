#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

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

    [TestCase(TerrainDistortionStyle.Classic)]
    [TestCase(TerrainDistortionStyle.Organic)]
    public void RoadSurfaceDoesNotInheritNeighborDistortion(TerrainDistortionStyle style)
    {
        var world = new TerrainTestWorld();
        var cache = new TerrainCellCache();
        var distortion = new TerrainDistortionSettings { DistortionStyle = style };
        TerrainCellSources sources = world.BuildSources(cache, distortion,
            OriginX, OriginY, Width, Height);
        var quad = new TerrainVertex[4];
        int adjacentRoads = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (cache.GetCellData(x + 1, y + 1).Type != CellType.Road) { continue; }
                bool movedNeighbor = Node(sources, x, y) != TerrainVertexOffset.Zero ||
                    Node(sources, x + 1, y) != TerrainVertexOffset.Zero ||
                    Node(sources, x + 1, y + 1) != TerrainVertexOffset.Zero ||
                    Node(sources, x, y + 1) != TerrainVertexOffset.Zero;
                if (!movedNeighbor && style == TerrainDistortionStyle.Classic) { continue; }
                adjacentRoads++;
                TerrainQuadBuilder.FillQuad(sources,
                    new TerrainQuadSite(x, y, OriginX + x, OriginY + y, 1f),
                    TerrainQuadLayer.Foreground, quad);
                Vector3[] expected = [new(x, y, 0), new(x + 1, y, 0),
                    new(x + 1, y + 1, 0), new(x, y + 1, 0)];
                for (int corner = 0; corner < 4; corner++)
                {
                    Assert.That(quad[corner].Position, Is.EqualTo(expected[corner]),
                        $"Road ({x},{y}) corner {corner} follows the neighboring rock distortion.");
                    Assert.That(quad[corner].UV5x, Is.Zero);
                }
            }
        }
        Assert.That(adjacentRoads, Is.GreaterThan(0), "Fixture must contain roads bordering displaced rock.");
    }

    // Накладка дверей читает вид из буфера клеток: пересобирать её надо,
    // только когда меняется состав дверей.
    private static TerrainVertexOffset Node(in TerrainCellSources sources, int x, int y) =>
        TerrainVertexDistortionCalculator.ComputeNode(
            sources.CellCache, sources.Distortion, x, y, sources.WorldWidth, sources.WorldHeight);

    [Test]
    public void DoorIndexReportsOnlyCompositionChanges()
    {
        var index = new TerrainDoorOverlayIndex();
        index.EnsureSize(1, 1);
        index.BeginFullBuild();
        index.RecordCell(0, 0, door: true);
        index.CompleteFullBuild();

        Assert.That(index.HasDoors, Is.True);
        Assert.That(index.RecordCell(0, 0, door: true), Is.False);
        Assert.That(index.RecordCell(0, 0, door: false), Is.True);
        Assert.That(index.HasDoors, Is.False);
    }

    [Test]
    public void RoadTextureArrivalReachesItsTypeRow()
    {
        var world = new TerrainTestWorld { RoadTextureReady = false };
        var cache = new TerrainCellCache();
        TerrainCellSources sources = world.BuildSources(
            cache, new TerrainDistortionSettings(),
            OriginX, OriginY, Width, Height);
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(Width, Height, 1f);
        builder.BuildFull(sources, OriginX, OriginY);
        Assert.That(builder.HasDoors, Is.True);

        world.RoadTextureReady = true;
        HashSet<CellType> changedTypes = [CellType.Road];
        cache.RefreshTextureMetadata(changedTypes, world.MapData, world.Textures, world.Atlases);
        builder.BuildTextureCells(
            TerrainCellTypeSet.Capture(changedTypes), sources, OriginX, OriginY);

        // Строка типа несёт текстуру, приехавшую после первой сборки.
        Assert.That(builder.Buffers.GetTypeRow(CellType.Road).AtlasWH & 0xFFFu, Is.Not.Zero);
    }

    // Текстура двери приезжает строкой типа: шейдер берёт её во всех
    // клетках и в накладке сразу, состав дверей не меняется.
    [Test]
    public void BuildTextureCells_LoadedDoorTexture_UpdatesTypeRowWithoutTouchingOverlay()
    {
        var world = new TerrainTestWorld { DoorTextureReady = false };
        var cache = new TerrainCellCache();
        TerrainCellSources sources = world.BuildSources(
            cache, new TerrainDistortionSettings(),
            OriginX, OriginY, Width, Height);
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(Width, Height, 1f);
        builder.BuildFull(sources, OriginX, OriginY);
        Assert.That(builder.HasDoors, Is.True, "Fixture must include doors.");
        Assert.That(builder.Buffers.GetTypeRow(CellType.BuildingDoor).AtlasWH & 0xFFFFu, Is.Zero);

        world.DoorTextureReady = true;
        HashSet<CellType> changedTypes = [CellType.BuildingDoor];
        cache.RefreshTextureMetadata(changedTypes, world.MapData, world.Textures, world.Atlases);
        builder.BuildTextureCells(
            TerrainCellTypeSet.Capture(changedTypes), sources, OriginX, OriginY);

        Assert.That(builder.Buffers.GetTypeRow(CellType.BuildingDoor).AtlasWH & 0xFFFFu, Is.Not.Zero);
        Assert.That(builder.DoorsTouched, Is.False,
            "A texture arrival does not change which cells are doors.");
    }

    [Test]
    public void BuildFull_RepeatedOnSameWindow_ProducesIdenticalTexels()
    {
        List<TerrainCell> first = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCell> second = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));

        AssertTexelsEqual(first, second, "повторная полная сборка");
    }

    // Последовательная сборка того же окна — независимый оракул: она не
    // касается планировщика вовсе. Расхождение с ней означает гонку.
    [Test]
    public void BuildFull_MatchesSequentialRegionBuild()
    {
        List<TerrainCell> parallel = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCell> sequential = BuildAndSnapshot(builder =>
        {
            TerrainCellSources sources = CreateSources();
            // A one-column range has only one Parallel.For iteration, so
            // independent columns cannot race in this reference build.
            for (int x = 0; x < Width; x++)
            {
                builder.BuildRegion(sources, OriginX, OriginY, x, 0, 1, Height);
            }
        });

        AssertTexelsEqual(parallel, sequential, "полная против последовательной");
    }

    [Test]
    public void BuildRegion_ParallelThresholdMatchesSerialColumns()
    {
        const int width = 96;
        const int height = 64;
        const int originX = 96;
        const int originY = 64;

        List<TerrainCell> parallel = BuildAndSnapshot(
            width,
            height,
            originX,
            originY,
            builder => builder.BuildRegion(
                CreateSources(width, height, originX, originY),
                originX,
                originY,
                0,
                0,
                width,
                height));
        List<TerrainCell> serial = BuildAndSnapshot(
            width,
            height,
            originX,
            originY,
            builder =>
            {
                TerrainCellSources sources = CreateSources(width, height, originX, originY);
                for (int x = 0; x < width; x++)
                {
                    builder.BuildRegion(sources, originX, originY, x, 0, 1, height);
                }
            });

        AssertTexelsEqual(parallel, serial, "parallel BuildRegion против последовательных колонок");
    }

    private static TerrainCellSources CreateSources()
    {
        return CreateSources(Width, Height, OriginX, OriginY);
    }

    private static TerrainCellSources CreateSources(int width, int height, int originX, int originY)
    {
        var world = new TerrainTestWorld();
        return world.BuildSources(
            new TerrainCellCache(),
            new TerrainDistortionSettings(),
            originX,
            originY,
            width,
            height);
    }

    private static List<TerrainCell> BuildAndSnapshot(
        System.Action<TerrainCellBuilder> build)
        => BuildAndSnapshot(build, Width, Height, OriginX, OriginY);

    private static List<TerrainCell> BuildAndSnapshot(
        int width,
        int height,
        int originX,
        int originY,
        System.Action<TerrainCellBuilder> build) =>
        BuildAndSnapshot(build, width, height, originX, originY);

    private static List<TerrainCell> BuildAndSnapshot(
        System.Action<TerrainCellBuilder> build,
        int width,
        int height,
        int originX,
        int originY)
    {
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(width, height, 1f);
        build(builder);

        // Окно и кайма: кайму пишет RefreshMargin, и её узлы читают краевые
        // клетки — она такая же часть результата.
        var snapshot = new List<TerrainCell>((width + 2) * (height + 2));
        for (int x = -1; x <= width; x++)
        {
            for (int y = -1; y <= height; y++)
            {
                snapshot.Add(builder.Buffers.GetCell(originX + x, originY + y));
            }
        }

        return snapshot;
    }

    private static void AssertTexelsEqual(
        List<TerrainCell> expected,
        List<TerrainCell> actual,
        string what)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count), what);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.That(
                actual[index],
                Is.EqualTo(expected[index]),
                $"{what}: клетка {index}");
        }
    }
}
