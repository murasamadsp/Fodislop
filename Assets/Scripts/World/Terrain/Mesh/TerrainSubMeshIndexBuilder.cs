#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fodinae.World.Terrain;

internal sealed class TerrainSubMeshIndexBuilder
{
    private const int IndicesPerQuad = 6;

    private int[] _columnOffsets = Array.Empty<int>();
    private int[] _totals = Array.Empty<int>();
    private int[][] _scratch = Array.Empty<int[]>();

    /// <param name="backgroundAtlases">Атлас фона по номеру клетки, или отрицательное число, если клетку не рисуем.</param>
    /// <param name="foregroundAtlases">Атлас переднего плана по номеру клетки.</param>
    /// <param name="verticesPerCell">Сколько вершин занимает клетка: оба слоя вместе.</param>
    public void Rebuild(
        int[] backgroundAtlases,
        int[] foregroundAtlases,
        int meshWidth,
        int meshHeight,
        int verticesPerCell,
        List<int>[] subMeshIndices)
    {
        int atlasCount = subMeshIndices.Length;
        if (atlasCount == 0 || meshWidth <= 0 || meshHeight <= 0)
        {
            for (int atlas = 0; atlas < atlasCount; atlas++)
            {
                subMeshIndices[atlas].Clear();
            }

            return;
        }

        int slotCount = meshWidth * atlasCount;
        if (_columnOffsets.Length < slotCount)
        {
            _columnOffsets = new int[slotCount];
        }

        if (_totals.Length < atlasCount)
        {
            _totals = new int[atlasCount];
        }

        Array.Clear(_columnOffsets, 0, slotCount);
        CountColumns(backgroundAtlases, foregroundAtlases, meshWidth, meshHeight, atlasCount);
        AccumulateColumns(meshWidth, atlasCount);
        EnsureScratch(atlasCount);
        WriteColumns(backgroundAtlases, foregroundAtlases, meshWidth, meshHeight, atlasCount, verticesPerCell);

        for (int atlas = 0; atlas < atlasCount; atlas++)
        {
            List<int> indices = subMeshIndices[atlas];
            indices.Clear();
            int written = _totals[atlas];
            if (written > 0)
            {
                indices.AddRange(new ArraySegment<int>(_scratch[atlas], 0, written));
            }
        }
    }

    public static void RebuildOverlay(
        int[] foregroundAtlases,
        bool[] overlayFlags,
        int meshWidth,
        int meshHeight,
        int verticesPerCell,
        List<int>[] overlaySubMeshIndices)
    {
        foreach (List<int> indices in overlaySubMeshIndices)
        {
            indices.Clear();
        }

        int totalQuads = meshWidth * meshHeight;

        for (int quadIndex = 0; quadIndex < totalQuads; quadIndex++)
        {
            int foregroundAtlas = foregroundAtlases[quadIndex];

            if (!overlayFlags[quadIndex] ||
                foregroundAtlas < 0 ||
                foregroundAtlas >= overlaySubMeshIndices.Length)
            {
                continue;
            }

            List<int> indices = overlaySubMeshIndices[foregroundAtlas];
            int baseIndex = (quadIndex * verticesPerCell) + 4;
            indices.Add(baseIndex);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex);
        }
    }

    private void CountColumns(
        int[] backgroundAtlases,
        int[] foregroundAtlases,
        int meshWidth,
        int meshHeight,
        int atlasCount)
    {
        int[] offsets = _columnOffsets;

        Parallel.For(0, meshWidth, x =>
        {
            int columnBase = x * atlasCount;
            int quadIndex = x * meshHeight;
            for (int y = 0; y < meshHeight; y++, quadIndex++)
            {
                int backgroundAtlas = backgroundAtlases[quadIndex];
                if ((uint)backgroundAtlas < (uint)atlasCount)
                {
                    offsets[columnBase + backgroundAtlas] += IndicesPerQuad;
                }

                int foregroundAtlas = foregroundAtlases[quadIndex];
                if ((uint)foregroundAtlas < (uint)atlasCount)
                {
                    offsets[columnBase + foregroundAtlas] += IndicesPerQuad;
                }
            }
        });
    }

    private void AccumulateColumns(int meshWidth, int atlasCount)
    {
        for (int atlas = 0; atlas < atlasCount; atlas++)
        {
            int running = 0;
            for (int x = 0; x < meshWidth; x++)
            {
                int slot = (x * atlasCount) + atlas;
                int columnLength = _columnOffsets[slot];
                _columnOffsets[slot] = running;
                running += columnLength;
            }

            _totals[atlas] = running;
        }
    }

    private void EnsureScratch(int atlasCount)
    {
        int previousLength = _scratch.Length;
        if (previousLength < atlasCount)
        {
            Array.Resize(ref _scratch, atlasCount);

            // Array.Resize оставляет добавленные ячейки пустыми ссылками, а
            // поле объявлено ненулевым. Заполняем сразу, чтобы дальше по коду
            // не приходилось спрашивать про null у того, чего там не бывает.
            for (int atlas = previousLength; atlas < atlasCount; atlas++)
            {
                _scratch[atlas] = Array.Empty<int>();
            }
        }

        for (int atlas = 0; atlas < atlasCount; atlas++)
        {
            int required = _totals[atlas];
            if (_scratch[atlas].Length < required)
            {
                _scratch[atlas] = new int[required];
            }
        }
    }

    private void WriteColumns(
        int[] backgroundAtlases,
        int[] foregroundAtlases,
        int meshWidth,
        int meshHeight,
        int atlasCount,
        int verticesPerCell)
    {
        int[] cursors = _columnOffsets;
        int[][] scratch = _scratch;

        Parallel.For(0, meshWidth, x =>
        {
            int columnBase = x * atlasCount;
            int quadIndex = x * meshHeight;
            for (int y = 0; y < meshHeight; y++, quadIndex++)
            {
                int baseIndex = quadIndex * verticesPerCell;

                int backgroundAtlas = backgroundAtlases[quadIndex];
                if ((uint)backgroundAtlas < (uint)atlasCount)
                {
                    int cursor = cursors[columnBase + backgroundAtlas];
                    WriteQuad(scratch[backgroundAtlas], cursor, baseIndex);
                    cursors[columnBase + backgroundAtlas] = cursor + IndicesPerQuad;
                }

                int foregroundAtlas = foregroundAtlases[quadIndex];
                if ((uint)foregroundAtlas < (uint)atlasCount)
                {
                    int cursor = cursors[columnBase + foregroundAtlas];
                    WriteQuad(scratch[foregroundAtlas], cursor, baseIndex + 4);
                    cursors[columnBase + foregroundAtlas] = cursor + IndicesPerQuad;
                }
            }
        });
    }

    private static void WriteQuad(int[] indices, int writeAt, int baseIndex)
    {
        indices[writeAt] = baseIndex;
        indices[writeAt + 1] = baseIndex + 3;
        indices[writeAt + 2] = baseIndex + 2;
        indices[writeAt + 3] = baseIndex + 2;
        indices[writeAt + 4] = baseIndex + 1;
        indices[writeAt + 5] = baseIndex;
    }
}
