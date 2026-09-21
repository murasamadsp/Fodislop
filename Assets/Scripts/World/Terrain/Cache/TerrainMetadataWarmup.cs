#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

// Прогрев метаданных перед сборкой клеток.
//
// ЗАЧЕМ. Полная сборка идёт через Parallel.For, а разрешение типа клетки —
// операция главного потока: она читает конфиг мира, пишет большую структуру в
// общий массив и дозаказывает недостающую текстуру. Пока это делалось прямо из
// FillCell, результат полной сборки зависел от того, какой поток успел первым.
//
// Прогрев снимает вопрос: все типы, которые сборке понадобятся, разрешаются
// здесь и последовательно, а сама сборка потом только читает.
//
// ЧТО ИМЕННО ГРЕЕТСЯ. Передний план берёт метаданные прямо из клетки кэша и
// ни о чём не спрашивает. Спрашивает только фоновый слой — типами из карты
// заливки, плюс две подстановки: Road под проходимым блоком здания и Empty
// под пустой клеткой (см. TerrainCellLayers.TryGetType).
public sealed class TerrainMetadataWarmup
{
    private readonly HashSet<CellType> _types = [];

    public void WarmRect(
        in TerrainCellSources sources,
        int startX,
        int endX,
        int startY,
        int endY)
    {
        _types.Clear();
        _types.Add(CellType.Road);
        _types.Add(CellType.Empty);
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                _types.Add(sources.FloodFill.Buffer[x, y]);
            }
        }

        Warm(sources);
    }

    private void Warm(in TerrainCellSources sources)
    {
        foreach (CellType type in _types)
        {
            if (type == CellType.Unloaded)
            {
                continue;
            }

            sources.CellCache.GetMetadata(
                type,
                sources.MapData,
                sources.TextureService,
                sources.Atlases);
        }
    }
}
