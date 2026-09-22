#nullable enable

using System.Collections.Generic;

namespace Kern.Persistence;

/// <summary>
/// Сброс грязных чанков на диск.
/// </summary>
///
/// Снимок берётся на главном потоке, пока кэш не меняется параллельно.
/// Массивы не клонируются: они отсоединяются от dirty-набора, а при следующей
/// записи в такой чанк PrepareForWrite делает ровно одну копию. Поэтому запись
/// видит стабильное состояние, а изменения после снимка остаются отдельной
/// dirty-версией.
///
/// Если запись не удалась, отметки возвращаются: следующее сохранение
/// повторит. Снимок и возврат — на главном потоке; сам WriteSnapshot можно
/// звать из любого потока, снимок стабилен из-за copy-on-write, а файл — под
/// замком ввода-вывода.
internal sealed class WorldLayerDirtyWriter<T>
    where T : unmanaged
{
    private readonly ChunkLruCache<T> _cache;
    private readonly WorldLayerFile<T> _file;
    private readonly int _chunkArea;

    public WorldLayerDirtyWriter(
        ChunkLruCache<T> cache,
        WorldLayerFile<T> file,
        int chunkArea)
    {
        _cache = cache;
        _file = file;
        _chunkArea = chunkArea;
    }

    public List<(int Index, T[] Chunk)> TakeSnapshot() => _cache.DetachDirtySnapshot();

    public void RestoreDirty(List<(int Index, T[] Chunk)> snapshot)
    {
        _cache.RestoreDirtySnapshot(EnumerateSnapshotIndices(snapshot));
    }

    public void Flush(bool flushToDisk)
    {
        List<(int Index, T[] Chunk)> snapshot = TakeSnapshot();
        WriteSnapshot(snapshot, flushToDisk);
    }

    public void WriteSnapshot(List<(int Index, T[] Chunk)> snapshot, bool flushToDisk)
    {
        try
        {
            foreach ((int index, T[] chunk) in snapshot)
            {
                _file.Save(index, chunk, _chunkArea);
            }

            if (!_file.Flush(flushToDisk))
            {
                _cache.CompleteDirtySnapshot(EnumerateSnapshotIndices(snapshot));
                return;
            }
        }
        catch
        {
            RestoreDirty(snapshot);
            throw;
        }

        _cache.CompleteDirtySnapshot(EnumerateSnapshotIndices(snapshot));
    }

    private static IEnumerable<int> EnumerateSnapshotIndices(
        IEnumerable<(int Index, T[] Chunk)> snapshot)
    {
        foreach ((int index, _) in snapshot)
        {
            yield return index;
        }
    }
}
