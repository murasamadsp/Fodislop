#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fodinae.World.Terrain;

/// <summary>
/// Собирает списки индексов сабмешей по приписке клеток к атласам.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ТИП. Строителю сетки нужны индексы, но не нужна кухня их
/// сборки: три прохода, таблица смещений по столбцам и переиспользуемые
/// массивы-черновики. Всё это состояние живёт только между проходами одного
/// вызова и никого снаружи не касается.
///
/// ЗАЧЕМ ТРИ ПРОХОДА ВМЕСТО ОДНОГО. Прежняя сборка дописывала индексы по
/// одному в <see cref="List{T}"/>: на окне 384x384 это порядка 1,8 миллиона
/// <c>Add</c> в один поток, каждый с проверкой ёмкости, — и всё это в
/// <c>LateUpdate</c>, то есть внутри кадра. Разложить работу по столбцам
/// нельзя, пока приёмник один и растёт: два потока подрались бы за хвост
/// списка.
///
/// Поэтому сперва считаем, сколько индексов даст каждый столбец каждому
/// атласу, потом префиксной суммой раздаём столбцам непересекающиеся отрезки,
/// и только потом пишем — уже параллельно, каждый столбец в свой отрезок.
/// Готовый массив уходит в список одним копированием.
///
/// ПОЧЕМУ ПОРЯДОК СОХРАНЁН. Клетки нумерованы по столбцам
/// (<c>x * height + y</c>), поэтому обход по x, затем по y — это ровно
/// прежний порядок возрастания номера клетки, а префиксная сумма по x его
/// удерживает. Фон перед передним планом — как было. Списки выходят теми же,
/// что давал последовательный проход.
/// </remarks>
internal sealed class TerrainSubMeshIndexBuilder
{
    /// <summary>Сколько индексов даёт один квад: два треугольника.</summary>
    private const int IndicesPerQuad = 6;

    private int[] _columnOffsets = Array.Empty<int>();
    private int[] _totals = Array.Empty<int>();
    private int[][] _scratch = Array.Empty<int[]>();

    /// <summary>
    /// Пересобирает списки индексов основной сетки.
    /// </summary>
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

    /// <summary>
    /// Пересобирает индексы накладки: только квады переднего плана, помеченные оверлейными.
    /// </summary>
    /// <remarks>
    /// Здесь прежний однопроходный вид остался намеренно. Оверлейных квадов
    /// единицы на всё окно, приписка почти всегда пуста, и раскладка по
    /// столбцам с таблицей смещений стоила бы дороже самой записи.
    /// </remarks>
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

    /// <remarks>
    /// Курсор записи держим прямо в таблице смещений: столбец трогает только
    /// свои ячейки, поэтому гонки нет, а отдельная таблица курсоров была бы
    /// второй такой же ради того же числа.
    /// </remarks>
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
