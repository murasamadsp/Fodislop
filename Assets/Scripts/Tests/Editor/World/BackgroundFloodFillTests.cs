#nullable enable

using System;
using System.Threading.Tasks;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

namespace Kern.Tests.World;

public sealed class BackgroundFloodFillTests
{
    private const int Width = 8;
    private const int Height = 6;

    // Регрессия: поколение посещённости сменялось на каждой волне, и две
    // соседние незагруженные клетки переписывали друг друга бесконечно —
    // TerrainRenderer.LateUpdate зависал на первом кадре мира.
    [Test]
    public void ComputeFull_WithUnloadedRegion_TerminatesAndFillsFromFloor()
    {
        var fill = new BackgroundFloodFill();
        fill.Allocate(Width, Height);
        var cells = new FloorColumnCells();

        bool finished = Task.Run(() => fill.ComputeFull(cells)).Wait(TimeSpan.FromSeconds(5));

        Assert.That(finished, Is.True, "Flood fill did not terminate.");
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Assert.That(fill.Buffer[x, y], Is.EqualTo(CellType.Rock), $"cell {x},{y}");
            }
        }
    }

    [Test]
    public void ComputeFull_RepeatedCalls_Terminate()
    {
        var fill = new BackgroundFloodFill();
        fill.Allocate(Width, Height);
        var cells = new FloorColumnCells();

        bool finished = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                fill.ComputeFull(cells);
            }
        }).Wait(TimeSpan.FromSeconds(10));

        Assert.That(finished, Is.True, "Repeated flood fill did not terminate.");
    }

    // Столбец 0 — проходимый пол, остальное ещё не загружено. Кеш адресуется
    // со сдвигом на рамку в одну клетку, как в TerrainRenderer.
    private sealed class FloorColumnCells : ICachedCellDataProvider
    {
        public CachedCellInfo GetCell(int x, int y)
        {
            return x == 1 && y >= 1 && y <= Height
                ? new CachedCellInfo { Type = CellType.Rock, Properties = CellConfigProperties.Passable }
                : new CachedCellInfo { Type = CellType.Unloaded, Properties = 0 };
        }
    }
}
