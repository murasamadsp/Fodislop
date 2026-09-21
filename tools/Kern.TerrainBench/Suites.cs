#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kern.World;
using Kern.World.Terrain;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.TerrainBench;

// Наборы замеров. Везде, где можно, вызывается настоящий код игры; старый
// путь буфера вершин оставлен только там, где он удалён из игры, — для
// сравнения порядков.
public static class Suites
{
    private const int Seed = 1337;

    public static void All(BenchRunner runner, int width, int height)
    {
        Scroll(runner, width, height);
        Pack(runner, width, height);
        Upload(runner, width, height);
        DirtyRegion(runner, width, height);
        DirtyRects(runner, width, height);
        CacheScroll(runner, width, height);
        FloodFill(runner, width, height);
        Masks(runner, width, height);
        Distortion(runner, width, height);
        FloodFillEquivalence(runner, width, height);
        CellTypeIndex(runner, width, height);
        QuadCatalogs(runner, width, height);
        Spatial(runner, width, height);
        SessionPipeline(runner, width, height);
    }

    // ── Сквозной сценарий: 600 шагов ходьбы с копанием ───────────────────
    //
    // Каждый шаг проходит весь процессорный конвейер террейна в том порядке,
    // что и TerrainRenderer: кэш со сдвигом и дозаполнением каймы, маски и
    // искажение со сдвигом, заливка фона со сдвигом, упаковка вошедшей полосы
    // в тексели, учёт грязной области и копирование под выгрузку. Каждый
    // десятый шаг — копание 3×3: заплатка кэша, масок, заливки и текселей.
    private static void SessionPipeline(BenchRunner runner, int width, int height)
    {
        runner.Suite = "session";
        const int Steps = 600;
        if (!runner.Wants("шаг конвейера террейна"))
        {
            return;
        }

        int minX = 5000;
        int minY = 7000;
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, minX, minY, Seed);
        var masks = new TerrainCellMaskCalculator();
        masks.EnsureCapacity(width, height);
        masks.PrecalculateFull(cache, width, height);
        var distortion = new TerrainVertexDistortionCalculator();
        distortion.EnsureCapacity(width, height);
        distortion.PrecalculateFull(cache, width, height, 10016, 40000);
        var provider = new CaveProvider(width, height, Seed) { OriginX = minX, OriginY = minY };
        var fill = new BackgroundFloodFill();
        fill.Allocate(width, height);
        fill.ComputeFull(provider);
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);
        var region = new TerrainDirtyRegion();
        region.Reset(width, height);
        region.Clear();

        var random = new Random(Seed);
        string[] stageNames = ["кэш: сдвиг и кайма", "маски", "искажение", "заливка фона", "упаковка полос", "копирование под выгрузку"];
        var stageTotals = new double[stageNames.Length];
        var samples = new List<double>(Steps);
        var digSamples = new List<double>(Steps / 10);
        long uploadedTexels = 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        for (int step = 0; step < Steps; step++)
        {
            int dx;
            int dy;
            do
            {
                dx = random.Next(-1, 2);
                dy = random.Next(-1, 2);
            }
            while (dx == 0 && dy == 0);

            long start = Stopwatch.GetTimestamp();
            minX += dx;
            minY += dy;
            provider.OriginX = minX;
            provider.OriginY = minY;
            long stage = Stopwatch.GetTimestamp();
            cache.ScrollTo(minX, minY, Seed);
            stage = Lap(stageTotals, 0, stage);
            masks.PrecalculateIncremental(cache, width, height, dx, dy);
            stage = Lap(stageTotals, 1, stage);
            distortion.PrecalculateIncremental(cache, width, height, dx, dy, 10016, 40000);
            stage = Lap(stageTotals, 2, stage);
            fill.ComputeScrolled(dx, dy, provider);
            stage = Lap(stageTotals, 3, stage);

            TerrainScrollBands bands = TerrainScrollBands.Resolve(width, height, dx, dy, neighbourMargin: 1);
            PackRect(texels, vertices, region, bands.ColumnBand.xMin, bands.ColumnBand.xMax, bands.ColumnBand.yMin, bands.ColumnBand.yMax, minX, minY, width, height);
            PackRect(texels, vertices, region, bands.RowBand.xMin, bands.RowBand.xMax, bands.RowBand.yMin, bands.RowBand.yMax, minX, minY, width, height);
            stage = Lap(stageTotals, 4, stage);
            uploadedTexels += CopyDirty(texels, region, width, height);
            Lap(stageTotals, 5, stage);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (step % 10 == 9)
            {
                long digStart = Stopwatch.GetTimestamp();
                int cx = 10 + random.Next(width - 20);
                int cy = 10 + random.Next(height - 20);
                cache.Dig(cx, cy, 3, 3);
                masks.PrecalculateRegion(cache, width, height, cx - 1, cy - 1, 5, 5);
                distortion.PrecalculateRegion(cache, width, height, cx - 1, cy - 1, 5, 5, 10016, 40000);
                fill.UpdateLocalRegion(cx - 1, cy - 1, 5, 5, provider);
                PackRect(texels, vertices, region, cx - 1, cx + 4, cy - 1, cy + 4, minX, minY, width, height);
                uploadedTexels += CopyDirty(texels, region, width, height);
                digSamples.Add(Stopwatch.GetElapsedTime(digStart).TotalMilliseconds);
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        runner.Record("шаг конвейера террейна", samples, allocated, gen0);
        runner.Record("копание 3×3 (заплатка конвейера)", digSamples, 0, 0);
        for (int i = 0; i < stageNames.Length; i++)
        {
            runner.Metric($"стадия шага: {stageNames[i]}", stageTotals[i] / Steps, "мс");
        }

        runner.Metric("выгружено текселей за шаг, в среднем", (double)uploadedTexels / Steps, "текс");
        runner.Metric("доля текстуры за шаг, в среднем", uploadedTexels * 100.0 / Steps / (width * height * 2), "%");
    }

    private static long Lap(double[] totals, int index, long since)
    {
        long now = Stopwatch.GetTimestamp();
        totals[index] += Stopwatch.GetElapsedTime(since, now).TotalMilliseconds;
        return now;
    }

    private static void PackRect(
        TexelArrays texels, TerrainVertex[] vertices, TerrainDirtyRegion region,
        int startX, int endX, int startY, int endY, int minX, int minY, int width, int height)
    {
        if (endX <= startX || endY <= startY)
        {
            return;
        }

        for (int x = startX; x < endX; x++)
        {
            int ringX = Ring(minX + x, width);
            for (int y = startY; y < endY; y++)
            {
                texels.PackCellAt(vertices, x, y, ringX, Ring(minY + y, height), width, height);
            }
        }

        region.MarkCells(Ring(minX + startX, width), Ring(minY + startY, height), endX - startX, endY - startY);
    }

    // Как TerrainCellDataTextures.Apply: прямоугольники, либо всё сразу,
    // если изменённое больше половины текстуры.
    private static long CopyDirty(TexelArrays texels, TerrainDirtyRegion region, int width, int height)
    {
        long texelCount = (long)width * height * 2;
        long uploaded;
        if (region.IsAll || region.Area * 2 >= texelCount)
        {
            texels.CopyAllTo(texels.Staging);
            uploaded = texelCount;
        }
        else
        {
            uploaded = 0;
            for (int i = 0; i < region.Count; i++)
            {
                RectInt rect = region[i];
                for (int row = rect.y; row < rect.yMax; row++)
                {
                    texels.CopyRowSpan(row, rect.x, rect.width, width);
                }

                uploaded += (long)rect.width * rect.height;
            }
        }

        region.Clear();
        return uploaded;
    }

    // ── Эквивалентность заливки: сдвиг против полного пересчёта ──────────
    private static void FloodFillEquivalence(BenchRunner runner, int width, int height)
    {
        runner.Suite = "flood-fill";
        if (!runner.Wants("расхождение сдвига с полным пересчётом"))
        {
            return;
        }

        foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (1, 1), (-1, -1) })
        {
            var provider = new CaveProvider(width, height, Seed) { OriginX = 5000, OriginY = 7000 };
            var scrolled = new BackgroundFloodFill();
            scrolled.Allocate(width, height);
            scrolled.ComputeFull(provider);
            provider.OriginX += dx;
            provider.OriginY += dy;
            scrolled.ComputeScrolled(dx, dy, provider);

            var full = new BackgroundFloodFill();
            full.Allocate(width, height);
            full.ComputeFull(provider);

            int mismatches = 0;
            int floorMismatches = 0;
            int borderMismatches = 0;
            int interiorMismatches = 0;
            int interiorCells = 0;
            const int BorderCells = 4;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool interior = x >= BorderCells && y >= BorderCells && x < width - BorderCells && y < height - BorderCells;
                    interiorCells += interior ? 1 : 0;
                    if (scrolled.Buffer[x, y] == full.Buffer[x, y])
                    {
                        continue;
                    }

                    mismatches++;
                    CachedCellInfo cell = provider.GetCell(x + 1, y + 1);
                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        floorMismatches++;
                    }

                    if (interior)
                    {
                        interiorMismatches++;
                    }
                    else
                    {
                        borderMismatches++;
                    }
                }
            }

            runner.Metric($"расхождение сдвига с полным пересчётом ({dx},{dy})", mismatches * 100.0 / (width * height), "%");
            runner.Metric($"  из них на проходимых клетках ({dx},{dy})", floorMismatches, "клеток");
            runner.Metric($"  из них у каймы ≤{BorderCells} ({dx},{dy})", borderMismatches, "клеток");
            runner.Metric($"  из них в глубине окна ({dx},{dy})", interiorMismatches * 100.0 / interiorCells, "% глубины");
        }
    }

    // ── Сдвиг окна камеры ────────────────────────────────────────────────
    // Плоский сдвиг буфера, каким террейн двигал вершины до кольцевой сетки.
    // Жил в TerrainMeshScroller, из игры удалён: кольцевая сетка двигает окно
    // за O(1) сменой двух индексов, копировать нечего. Оставлен здесь, чтобы
    // строка «старый» в сравнении продолжала мерить то же, что и раньше.
    private static void LegacyBufferScroll<T>(
        T[] buffer, int width, int height, int elementsPerCell, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        int keptWidth = width - Math.Abs(dx);
        int keptHeight = height - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int sourceY = dy > 0 ? dy : 0;
        int targetY = dy > 0 ? 0 : -dy;
        int runLength = keptHeight * elementsPerCell;
        if (dx >= 0)
        {
            for (int x = 0; x < keptWidth; x++)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
        else
        {
            int firstTargetX = -dx;
            for (int x = firstTargetX + keptWidth - 1; x >= firstTargetX; x--)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
    }

    private static void CopyColumn<T>(
        T[] buffer, int targetX, int sourceX, int height, int elementsPerCell,
        int sourceY, int targetY, int runLength)
    {
        int sourceOffset = ((sourceX * height) + sourceY) * elementsPerCell;
        int targetOffset = ((targetX * height) + targetY) * elementsPerCell;
        Array.Copy(buffer, sourceOffset, buffer, targetOffset, runLength);
    }

    private static void Scroll(BenchRunner runner, int width, int height)
    {
        runner.Suite = "scroll";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var atlasGrid = new TerrainRingGrid<int>();
        var doorGrid = new TerrainRingGrid<bool>();
        atlasGrid.EnsureSize(width, height);
        doorGrid.EnsureSize(width, height);

        foreach ((int dx, int dy, string label) in new[] { (1, 0, "x"), (0, 1, "y"), (1, 1, "диагональ"), (-3, 2, "рывок -3,+2") })
        {
            runner.Run($"старый: буфер вершин + позиции, {label}", () =>
            {
                LegacyBufferScroll(vertices, width, height, 8, dx, dy);
                ShiftPositionsOld(vertices, width, height, 8, 1f, dx, dy);
            });
            runner.Run($"новый: атласы и двери, {label}", () =>
            {
                atlasGrid.Scroll(dx, dy);
                doorGrid.Scroll(dx, dy);
            });
        }

        RectInt packBand = TerrainScrollBands.Resolve(width, height, 1, 0, neighbourMargin: 1).ColumnBand;
        int bandStart = packBand.xMin;
        int bandLength = packBand.width;
        var texels = new TexelArrays(width, height);
        runner.Run($"новый: упаковка полосы {bandLength}×{height}", () =>
        {
            for (int x = bandStart; x < bandStart + bandLength; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            }
        });
    }

    // ── Упаковка квадов в тексели ────────────────────────────────────────
    private static void Pack(BenchRunner runner, int width, int height)
    {
        runner.Suite = "pack";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);

        runner.Run("один PackQuad", () =>
        {
            GC.KeepAlive(TerrainCellDataPacker.PackQuad(vertices.AsSpan(0, 4), 1));
        });
        runner.Run("вся сетка последовательно", () =>
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            }
        });
        runner.Run("вся сетка Parallel.For по столбцам", () =>
        {
            Parallel.For(0, width, x =>
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            });
        });
        runner.Run("вся сетка Parallel.For по строкам", () =>
        {
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            });
        });
        runner.Run("вся сетка кольцевой адрес (сдвиг окна 5000,7000)", () =>
        {
            for (int x = 0; x < width; x++)
            {
                int ringX = Ring(5000 + x, width);
                for (int y = 0; y < height; y++)
                {
                    texels.PackCellAt(vertices, x, y, ringX, Ring(7000 + y, height), width, height);
                }
            }
        });
    }

    // ── Подготовка к выгрузке на GPU ─────────────────────────────────────
    private static void Upload(BenchRunner runner, int width, int height)
    {
        runner.Suite = "upload";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        byte[] vertexStaging = new byte[vertices.Length * Marshal.SizeOf<TerrainVertex>()];
        var texels = new TexelArrays(width, height);
        byte[] texelStaging = new byte[texels.TotalBytes];

        runner.Run($"старый: весь буфер вершин {vertexStaging.Length / 1048576.0:F2} МБ", () =>
            MemoryMarshal.AsBytes(vertices.AsSpan()).CopyTo(vertexStaging));
        runner.Run($"новый: все 7 текстур {texelStaging.Length / 1048576.0:F2} МБ", () => texels.CopyAllTo(texelStaging));

        int rows = height * 2;
        var patch = new Vector4[16 * rows];
        runner.Run("новый: полоса 2×2H одной float-текстуры", () =>
        {
            for (int row = 0; row < rows; row++)
            {
                Array.Copy(texels.World, (row * width) + width - 2, patch, row * 16, 2);
            }
        });
        runner.Run("новый: полоса 2×2H всех 7 текстур", () =>
        {
            for (int row = 0; row < rows; row++)
            {
                texels.CopyRowSpan(row, width - 2, 2, width);
            }
        });
        runner.Run("новый: заплатка 3×3 клетки всех 7 текстур", () =>
        {
            for (int row = 40; row < 46; row++)
            {
                texels.CopyRowSpan(row, 60, 3, width);
            }
        });
    }

    // ── Учёт грязных областей (настоящий TerrainDirtyRegion) ──────────────
    private static void DirtyRegion(BenchRunner runner, int width, int height)
    {
        runner.Suite = "dirty-region";
        var region = new TerrainDirtyRegion();
        var random = new Random(Seed);

        runner.Run("полоса x + полоса y (диагональный шаг)", () =>
        {
            region.MarkCells(width - 2, 0, 2, height);
            region.MarkCells(0, height - 2, width, 2);
        }, () => { region.Reset(width, height); region.Clear(); });
        runner.Run("полоса на шве кольца", () =>
        {
            region.MarkCells(width - 1, height - 1, 3, height);
        }, () => { region.Reset(width, height); region.Clear(); });
        runner.Run("200 рассыпанных заплаток 1×1", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                region.MarkCells(random.Next(width), random.Next(height), 1, 1);
            }
        }, () => { region.Reset(width, height); region.Clear(); });

        // Качество, а не время: какую долю текстуры в итоге пришлось бы выгрузить.
        region.Reset(width, height);
        region.Clear();
        region.MarkCells(width - 2, 0, 2, height);
        region.MarkCells(0, height - 2, width, 2);
        runner.Metric("диагональный шаг: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
        runner.Metric("диагональный шаг: прямоугольников", region.Count, "шт");
        region.Clear();
        for (int i = 0; i < 20; i++)
        {
            region.MarkCells((width / 2) + random.Next(8), (height / 2) + random.Next(8), 1, 1);
        }

        runner.Metric("20 заплаток в пятне 8×8: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
        region.Clear();
        for (int i = 0; i < 200; i++)
        {
            region.MarkCells(random.Next(width), random.Next(height), 1, 1);
        }

        runner.Metric("200 заплаток по всей сетке: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
    }

    // ── DirtyRectSet (настоящий) ─────────────────────────────────────────
    private static void DirtyRects(BenchRunner runner, int width, int height)
    {
        runner.Suite = "dirty-rect-set";
        var set = new DirtyRectSet();
        var bounds = new RectInt(1000, 2000, width, height);
        var random = new Random(Seed);
        runner.Run("64 случайных прямоугольника копания", () =>
        {
            for (int i = 0; i < 64; i++)
            {
                set.Add(new RectInt(1000 + random.Next(width), 2000 + random.Next(height), 1 + random.Next(3), 1 + random.Next(3)), bounds);
            }
        }, set.Clear);
        runner.Run("TotalArea после 8 прямоугольников", () => GC.KeepAlive(set.TotalArea), () =>
        {
            set.Clear();
            for (int i = 0; i < 8; i++)
            {
                set.Add(new RectInt(1000 + (i * 20), 2000 + (i * 9), 4, 4), bounds);
            }
        });
    }

    // ── Кольцевой сдвиг кэша ───────────────────────────────────────────────
    private static void CacheScroll(BenchRunner runner, int width, int height)
    {
        runner.Suite = "cache-scroll";
        var ring = new TerrainRingGrid<CachedCellData>();
        ring.EnsureSize(width + 2, height + 2);
        runner.Run("TerrainRingGrid сдвиг x", () => ring.Scroll(1, 0));

        // FillQuad адресует кольцевые сетки девять раз на квад: четыре узла
        // искажения, три маски, атласы и флаг двери. Каждый индекс — два
        // целочисленных деления по размеру окна.
        var nodes = new TerrainRingGrid<int>();
        nodes.EnsureSize(width + 1, height + 1);
        runner.Run("девять чтений по кольцевому адресу на клетку", () =>
        {
            int sink = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    sink += nodes[x, y] + nodes[x + 1, y] + nodes[x, y + 1] + nodes[x + 1, y + 1];
                    sink += nodes[x, y] + nodes[x, y] + nodes[x, y] + nodes[x, y] + nodes[x, y];
                }
            }

            GC.KeepAlive(sink);
        });
        runner.Metric("размер CachedCellData", Marshal.SizeOf<CachedCellData>(), "Б");
        runner.Metric("кэш клеток целиком", Marshal.SizeOf<CachedCellData>() * (width + 2) * (height + 2) / 1048576.0, "МБ");
    }

    // ── Заливка фона (настоящий BackgroundFloodFill) ─────────────────────
    private static void FloodFill(BenchRunner runner, int width, int height)
    {
        runner.Suite = "flood-fill";
        var provider = new CaveProvider(width, height, Seed);
        var fill = new BackgroundFloodFill();
        fill.Allocate(width, height);
        runner.Run("ComputeFull", () => fill.ComputeFull(provider));
        runner.Run("ComputeScrolled +1,0", () => fill.ComputeScrolled(1, 0, provider));
        runner.Run("ComputeScrolled +1,+1", () => fill.ComputeScrolled(1, 1, provider));
        runner.Run("ComputeScrolled +13,0", () => fill.ComputeScrolled(13, 0, provider));
        runner.Run("ComputeScrolled +29,+29", () => fill.ComputeScrolled(29, 29, provider));
        runner.Run("UpdateLocalRegion 3×3", () => fill.UpdateLocalRegion(width / 2, height / 2, 3, 3, provider));
        runner.Run("UpdateLocalRegion 16×16", () => fill.UpdateLocalRegion(width / 3, height / 3, 16, 16, provider));
        // Приход чанков сливается в один прямоугольник во всё окно: заплатка
        // тогда идёт последовательно там, где полный путь идёт параллельно.
        runner.Run("UpdateLocalRegion во всё окно", () => fill.UpdateLocalRegion(0, 0, width, height, provider));
    }

    // ── Маски клеток (настоящий TerrainCellMaskCalculator) ───────────────
    private static void Masks(BenchRunner runner, int width, int height)
    {
        runner.Suite = "masks";
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, 5000, 7000, Seed);
        var masks = new TerrainCellMaskCalculator();
        masks.EnsureCapacity(width, height);
        runner.Run("PrecalculateFull", () => masks.PrecalculateFull(cache, width, height));
        runner.Run("PrecalculateIncremental +1,0", () => masks.PrecalculateIncremental(cache, width, height, 1, 0));
        runner.Run("PrecalculateRegion 5×5", () => masks.PrecalculateRegion(cache, width, height, width / 2, height / 2, 5, 5));
        runner.Run("PrecalculateRegion во всё окно", () => masks.PrecalculateRegion(cache, width, height, 0, 0, width, height));
    }

    // ── Искажение сетки (настоящий TerrainVertexDistortionCalculator) ─────
    private static void Distortion(BenchRunner runner, int width, int height)
    {
        runner.Suite = "distortion";
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, 5000, 7000, Seed);
        var distortion = new TerrainVertexDistortionCalculator();
        distortion.EnsureCapacity(width, height);
        runner.Run("PrecalculateFull", () => distortion.PrecalculateFull(cache, width, height, 10016, 40000));
        runner.Run("PrecalculateIncremental +1,0", () => distortion.PrecalculateIncremental(cache, width, height, 1, 0, 10016, 40000));
        runner.Run("PrecalculateRegion 5×5", () => distortion.PrecalculateRegion(cache, width, height, width / 2, height / 2, 5, 5, 10016, 40000));
        runner.Run("PrecalculateRegion во всё окно", () => distortion.PrecalculateRegion(cache, width, height, 0, 0, width, height, 10016, 40000));
    }

    // ── Каталоги типов, которые FillQuad опрашивает на каждый квад ───────
    //
    // Все эти ответы зависят ТОЛЬКО от типа клетки, но спрашиваются на каждой
    // клетке и на каждом из двух слоёв. Замер показывает цену одного прохода
    // по окну и цену того же прохода через таблицу, разрешённую по типу.
    private static void QuadCatalogs(BenchRunner runner, int width, int height)
    {
        runner.Suite = "quad-catalogs";
        var random = new Random(Seed);
        var types = new CellType[width * height];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = (CellType)(1 + random.Next(60));
        }

        runner.Run("каталоги на клетку, оба слоя", () =>
        {
            int sink = 0;
            for (int i = 0; i < types.Length; i++)
            {
                CellType type = types[i];
                for (int layer = 0; layer < 2; layer++)
                {
                    sink += MapCellConfigCatalog.IsRoundableLoose(type) ? 1 : 0;
                    sink += MapCellConfigCatalog.IsRoad(type) ? 1 : 0;
                    sink += MapCellConfigCatalog.IsBuildingOrArtificialBlock(type) ? 1 : 0;
                    sink += TerrainSheetCatalog.IsContinuousSheet(type) ? 1 : 0;
                    sink += TerrainReliefRimCatalog.ParticipatesInRim(type) ? 1 : 0;
                    sink += TerrainDecalCatalog.IsGroundSurface(type) ? 1 : 0;
                    sink += (int)TerrainAnimationProfileCatalog.Get(type, 1f).Profile;
                }
            }

            GC.KeepAlive(sink);
        });

        var roundable = new bool[65536];
        var road = new bool[65536];
        var artificial = new bool[65536];
        var sheet = new bool[65536];
        var rim = new bool[65536];
        var ground = new bool[65536];
        var profile = new int[65536];
        for (int value = 0; value < 65536; value++)
        {
            var type = (CellType)value;
            roundable[value] = MapCellConfigCatalog.IsRoundableLoose(type);
            road[value] = MapCellConfigCatalog.IsRoad(type);
            artificial[value] = MapCellConfigCatalog.IsBuildingOrArtificialBlock(type);
            sheet[value] = TerrainSheetCatalog.IsContinuousSheet(type);
            rim[value] = TerrainReliefRimCatalog.ParticipatesInRim(type);
            ground[value] = TerrainDecalCatalog.IsGroundSurface(type);
            profile[value] = (int)TerrainAnimationProfileCatalog.Get(type, 1f).Profile;
        }

        runner.Run("та же выборка из таблицы по типу", () =>
        {
            int sink = 0;
            for (int i = 0; i < types.Length; i++)
            {
                int type = (int)types[i];
                for (int layer = 0; layer < 2; layer++)
                {
                    sink += roundable[type] ? 1 : 0;
                    sink += road[type] ? 1 : 0;
                    sink += artificial[type] ? 1 : 0;
                    sink += sheet[type] ? 1 : 0;
                    sink += rim[type] ? 1 : 0;
                    sink += ground[type] ? 1 : 0;
                    sink += profile[type];
                }
            }

            GC.KeepAlive(sink);
        });
    }

    // ── Индекс типов клеток (настоящий CellTypeSpatialIndex) ─────────────
    //
    // Заплатка зовёт Set на ЗАПОЛНЕННОМ индексе, где тип почти всегда тот же;
    // полная сборка — после Clear, на пустом. Контрольный замер рядом показывает
    // цену того же прохода через словарь: ради неё обратная сторона индекса и
    // лежит плотным массивом по кольцевому адресу.
    private static void CellTypeIndex(BenchRunner runner, int width, int height)
    {
        runner.Suite = "cell-type-index";
        var random = new Random(Seed);
        var types = new CellType[width * height];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = (CellType)(1 + random.Next(24));
        }

        var index = new CellTypeSpatialIndex();
        index.EnsureWindow(width, height);
        void Fill()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    index.Set(
                        TerrainCoordinateKey.Pack(5000 + x, 7000 + y),
                        types[(x * height) + y]);
                }
            }
        }

        runner.Run("Set во всё окно после Clear (полная сборка)", Fill, index.Clear);
        Fill();
        runner.Run("Set во всё окно поверх заполненного (заплатка)", Fill);
        // Обратная сторона сделки: запись стала двумя записями в массив, а
        // вопрос «какие клетки этих типов» — проходом по окну. Проход платится
        // по приходу текстуры, запись — тысячами в каждом кадре.
        var wanted = new HashSet<CellType> { (CellType)3, (CellType)17, (CellType)42 };
        var collected = new List<(long Key, CellType Type)>(4096);
        runner.Run("проход по окну за клетками трёх типов", () =>
        {
            collected.Clear();
            index.CollectEntries(wanted, collected);
        });

        var alternate = new CellType[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            alternate[i] = (CellType)(1 + ((int)types[i] % 24));
        }

        bool flip = false;
        runner.Run("Set во всё окно со сменой типа каждой клетки", () =>
        {
            CellType[] source = flip ? types : alternate;
            flip = !flip;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    index.Set(TerrainCoordinateKey.Pack(5000 + x, 7000 + y), source[(x * height) + y]);
                }
            }
        });

        Fill();
        var probe = new Dictionary<long, CellType>(width * height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                probe[TerrainCoordinateKey.Pack(5000 + x, 7000 + y)] = types[(x * height) + y];
            }
        }

        runner.Run("контроль: один поиск по обратному словарю на клетку", () =>
        {
            int hits = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (probe.TryGetValue(TerrainCoordinateKey.Pack(5000 + x, 7000 + y), out CellType t) &&
                        t == types[(x * height) + y])
                    {
                        hits++;
                    }
                }
            }

            GC.KeepAlive(hits);
        });
    }

    // ── Пространственный индекс сущностей (настоящий SpatialShardGrid) ────
    private static void Spatial(BenchRunner runner, int width, int height)
    {
        runner.Suite = "spatial";
        const int Entities = 5000;
        var random = new Random(Seed);
        var items = new object[Entities];
        var positions = new Vector2[Entities];
        for (int i = 0; i < Entities; i++)
        {
            items[i] = new object();
            positions[i] = new Vector2(random.Next(10016), random.Next(40000));
        }

        var grid = new Kern.World.SpatialShardGrid<object>();
        var results = new List<object>(256);
        runner.Run($"Insert {Entities}", () =>
        {
            for (int i = 0; i < Entities; i++)
            {
                grid.Insert(items[i], positions[i]);
            }
        }, grid.Clear);
        runner.Run($"Update {Entities} на соседнюю клетку", () =>
        {
            for (int i = 0; i < Entities; i++)
            {
                positions[i] = new Vector2(positions[i].x + 1, positions[i].y);
                grid.Update(items[i], positions[i]);
            }
        });
        var view = new Rect(5000, 20000, width, height);
        runner.Run($"QueryRect окно {width}×{height}", () =>
        {
            results.Clear();
            grid.QueryRect(view, results);
        });
        runner.Run("QueryRadius 64", () =>
        {
            results.Clear();
            grid.QueryRadius(new Vector2(5000, 20000), 64, results);
        });
    }

    // ── Общее ────────────────────────────────────────────────────────────
    private static int Ring(int value, int size)
    {
        int remainder = value % size;
        return remainder < 0 ? remainder + size : remainder;
    }

    private static TerrainVertex[] SyntheticVertices(int width, int height)
    {
        var random = new Random(Seed);
        var vertices = new TerrainVertex[width * height * 8];
        Vector2[] corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        for (int quad = 0; quad < vertices.Length / 4; quad++)
        {
            var color = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            var atlasRect = new Vector4(random.NextSingle(), random.NextSingle(), 0.0625f, 0.0625f);
            for (int i = 0; i < 4; i++)
            {
                ref TerrainVertex vertex = ref vertices[(quad * 4) + i];
                vertex.Position = new Vector3(i, i, 0);
                vertex.Color = color;
                vertex.UV0 = corners[i];
                vertex.UV1 = atlasRect;
                vertex.UV2 = new Vector4(0.015625f, 0.015625f, 1, 1);
                vertex.UV3 = new Vector4(random.Next(10000), random.Next(40000), 0, 0);
                vertex.UV4 = new Vector4(0, 0, 0, 0);
                vertex.UV5 = new Vector4(0, i, i, 0);
                vertex.UV6 = new Vector4(random.Next(16777215), 64, 0, 0);
            }
        }

        return vertices;
    }

    // Удалённый из игры TerrainMeshScroller.ShiftPositions — старый путь.
    private static void ShiftPositionsOld(TerrainVertex[] buffer, int meshWidth, int meshHeight, int verticesPerCell, float cellSize, int dx, int dy)
    {
        int keptWidth = meshWidth - Math.Abs(dx);
        int keptHeight = meshHeight - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int firstX = dx > 0 ? 0 : -dx;
        int firstY = dy > 0 ? 0 : -dy;
        float shiftX = dx * cellSize;
        float shiftY = dy * cellSize;
        Parallel.For(firstX, firstX + keptWidth, x =>
        {
            int start = ((x * meshHeight) + firstY) * verticesPerCell;
            int end = start + (keptHeight * verticesPerCell);
            for (int i = start; i < end; i++)
            {
                ref TerrainVertex vertex = ref buffer[i];
                vertex.Position.x -= shiftX;
                vertex.Position.y -= shiftY;
            }
        });
    }

    // Клетки окна для заливки фона: локальные (x, y) со смещением каймы кэша.
    private sealed class CaveProvider(int width, int height, int seed) : ICachedCellDataProvider
    {
        public int OriginX { get; set; } = 5000;

        public int OriginY { get; set; } = 7000;

        public int Width => width;

        public int Height => height;

        public CachedCellInfo GetCell(int x, int y)
        {
            CachedCellData cell = TerrainCellCache.CellAt(OriginX + x - 1, OriginY + y - 1, seed);
            return new CachedCellInfo { Type = cell.Type, Properties = cell.Properties };
        }
    }

    // Семь массивов текселей в раскладке TerrainCellDataTextures.
    private sealed class TexelArrays
    {
        public readonly Color32[] Color;
        public readonly Color32[] Meta;
        public readonly TerrainHalfTexel[] AtlasRect;
        public readonly TerrainHalfTexel[] TileSize;
        public readonly TerrainHalfTexel[] Animation;
        public readonly Vector4[] World;
        public readonly Vector4[] Glow;
        private readonly Color32[] _patchColor = new Color32[1024];
        private readonly Vector4[] _patchWorld = new Vector4[1024];
        private readonly TerrainHalfTexel[] _patchHalf = new TerrainHalfTexel[1024];

        public byte[] Staging => _staging ??= new byte[TotalBytes];

        private byte[]? _staging;

        public TexelArrays(int width, int height)
        {
            int count = width * height * TerrainCellDataPacker.LayersPerCell;
            Color = new Color32[count];
            Meta = new Color32[count];
            AtlasRect = new TerrainHalfTexel[count];
            TileSize = new TerrainHalfTexel[count];
            Animation = new TerrainHalfTexel[count];
            World = new Vector4[count];
            Glow = new Vector4[count];
        }

        public int TotalBytes => (Color.Length * 4 * 2) + (AtlasRect.Length * 8 * 3) + (World.Length * 16 * 2);

        public void PackCell(TerrainVertex[] vertices, int x, int y, int width, int height) =>
            PackCellAt(vertices, x, y, x, y, width, height);

        public void PackCellAt(TerrainVertex[] vertices, int x, int y, int ringX, int ringY, int width, int height)
        {
            int quad = (x * height) + y;
            for (int layer = 0; layer < TerrainCellDataPacker.LayersPerCell; layer++)
            {
                TerrainCellTexels texels = TerrainCellDataPacker.PackQuad(vertices.AsSpan((quad * 8) + (layer * 4), 4), 0);
                int index = TerrainCellDataPacker.TexelIndex(ringX, ringY, layer, width);
                Color[index] = texels.Color;
                Meta[index] = texels.Meta;
                AtlasRect[index] = texels.AtlasRect;
                TileSize[index] = texels.TileSize;
                Animation[index] = texels.Animation;
                World[index] = texels.World;
                Glow[index] = texels.Glow;
            }
        }

        public void CopyAllTo(byte[] target)
        {
            int offset = 0;
            offset += CopyBytes(Color, target, offset);
            offset += CopyBytes(Meta, target, offset);
            offset += CopyBytes(AtlasRect, target, offset);
            offset += CopyBytes(TileSize, target, offset);
            offset += CopyBytes(Animation, target, offset);
            offset += CopyBytes(World, target, offset);
            CopyBytes(Glow, target, offset);
        }

        public void CopyRowSpan(int row, int x, int count, int width)
        {
            int start = (row * width) + x;
            Array.Copy(Color, start, _patchColor, 0, count);
            Array.Copy(Meta, start, _patchColor, 0, count);
            Array.Copy(AtlasRect, start, _patchHalf, 0, count);
            Array.Copy(TileSize, start, _patchHalf, 0, count);
            Array.Copy(Animation, start, _patchHalf, 0, count);
            Array.Copy(World, start, _patchWorld, 0, count);
            Array.Copy(Glow, start, _patchWorld, 0, count);
        }

        private static int CopyBytes<T>(T[] source, byte[] target, int offset)
            where T : struct
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(source.AsSpan());
            bytes.CopyTo(target.AsSpan(offset));
            return bytes.Length;
        }
    }
}
