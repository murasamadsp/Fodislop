#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.World.Lighting.Diagnostics;

public struct InvalidationFrameRecord
{
    public ulong FrameIndex;
    public LightingInvalidationFlags Triggers;
    public string Reason;

    // Массивы переживают запись и переиспользуются, поэтому их длина — это
    // вместимость, а не число проходов. Читать следует ExecutedCount и
    // SkippedCount, иначе в хвосте окажутся пустые строки прошлой записи.
    public string[] ExecutedPasses;
    public string[] SkippedPasses;
    public int ExecutedCount;
    public int SkippedCount;
}

/// <summary>
/// Ring buffer storing the last 64 frames of lighting invalidation events and pass execution reasons.
/// Allows instant post-mortem analysis of why any pass executed or skipped.
/// </summary>
public sealed class LightingInvalidationJournal
{
    private const int Capacity = 64;
    private readonly InvalidationFrameRecord[] _records = new InvalidationFrameRecord[Capacity];
    private int _head;
    private int _count;

    public int Count => _count;

    // Списки проходов копируются в массив слота, а не принимаются готовыми.
    //
    // Слотов ровно Capacity, и каждый переживает вызов: вызывающему незачем
    // отдавать свежий массив на каждое решение освещения — а он отдавал,
    // через ToArray() на своём накопителе. Массив слота растёт только когда
    // проходов стало больше, чем помещалось, то есть на прогретом кольце не
    // растёт вовсе.
    public void Record(
        ulong frameIndex,
        LightingInvalidationFlags triggers,
        string reason,
        IReadOnlyList<string> executedPasses,
        IReadOnlyList<string> skippedPasses)
    {
        ref InvalidationFrameRecord slot = ref _records[_head];
        slot.FrameIndex = frameIndex;
        slot.Triggers = triggers;
        slot.Reason = reason;
        slot.ExecutedPasses = CopyInto(slot.ExecutedPasses, executedPasses);
        slot.SkippedPasses = CopyInto(slot.SkippedPasses, skippedPasses);
        slot.ExecutedCount = executedPasses.Count;
        slot.SkippedCount = skippedPasses.Count;

        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    // Длина массива слота — вместимость, а не число проходов: вместимость
    // сохраняется между записями, значимую длину несут ExecutedCount и
    // SkippedCount.
    //
    // Слот переписывается только через Capacity записей, а читатель берёт
    // запись и рисует её в том же кадре, поэтому содержимое под ним не
    // меняется. Держать запись дольше кадра нельзя.
    private static string[] CopyInto(string[]? buffer, IReadOnlyList<string> source)
    {
        string[] target = buffer != null && buffer.Length >= source.Count
            ? buffer
            : new string[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            target[i] = source[i];
        }

        for (int i = source.Count; i < target.Length; i++)
        {
            target[i] = string.Empty;
        }

        return target;
    }

    public List<InvalidationFrameRecord> GetRecent(int maxCount)
    {
        var result = new List<InvalidationFrameRecord>(Math.Min(_count, maxCount));
        int toFetch = Math.Min(_count, maxCount);

        for (int i = 0; i < toFetch; i++)
        {
            int index = (_head - 1 - i + Capacity * 2) % Capacity;
            result.Add(_records[index]);
        }

        return result;
    }
}
