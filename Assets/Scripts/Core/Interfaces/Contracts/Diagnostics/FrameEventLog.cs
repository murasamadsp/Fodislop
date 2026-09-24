#nullable enable

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Kern.Core.Interfaces.Diagnostics;

/// <summary>
/// Журнал того, что случилось в кадрах перед провисом: редкие тяжёлые события
/// (пересоздание текстур, новый атлас, новые материалы, декодирование,
/// пересоздание ресурсов света) и разбор подсистем (<see cref="IFrameEventSource"/>).
/// </summary>
///
/// Провис, в котором главный поток ждёт поток рендера, не виден маркерам
/// скриптов: работу отдали рендеру кадром-двумя раньше, и в провисшем кадре
/// скрипты почти ничего не стоят. Такие события редки и сами по себе дёшевы
/// на главном потоке, но тяжелы для рендера — поэтому их и записываем: отчёт
/// о провисе (FrameStallMonitor) печатает всё, что случилось за несколько
/// кадров до него.
///
/// Подсистемы с собственным разбором кадра (террейн) не печатают свои строки
/// со своими порогами, а регистрируются источником: их разбор выходит в том же
/// отчёте, рядом с событиями.
public static class FrameEventLog
{
    private const int Capacity = 64;

    private static readonly int[] _frames = new int[Capacity];
    private static readonly string?[] _texts = new string?[Capacity];
    private static readonly List<IFrameEventSource> _sources = [];
    private static readonly object _gate = new();
    private static int _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        lock (_gate)
        {
            System.Array.Clear(_texts, 0, Capacity);
            _next = 0;
            _sources.Clear();
        }
    }

    public static void Record(string text)
    {
        int frame = Time.frameCount;
        lock (_gate)
        {
            _frames[_next] = frame;
            _texts[_next] = text;
            _next = (_next + 1) % Capacity;
        }
    }

    public static void AddSource(IFrameEventSource source)
    {
        lock (_gate)
        {
            if (!_sources.Contains(source))
            {
                _sources.Add(source);
            }
        }
    }

    public static void RemoveSource(IFrameEventSource source)
    {
        lock (_gate)
        {
            _sources.Remove(source);
        }
    }

    /// <summary>Разделитель и смещение кадра перед очередной записью.</summary>
    public static StringBuilder AppendEntryPrefix(StringBuilder text, int writtenBefore, int frame, int lastFrame) =>
        text.Append(writtenBefore == 0 ? " · события: " : "; ")
            .Append('[').Append(frame - lastFrame).Append("] ");

    /// <summary>Дописать события и разбор источников за кадры [firstFrame, lastFrame]; вернуть их число.</summary>
    public static int AppendRange(StringBuilder text, int firstFrame, int lastFrame)
    {
        int count = 0;
        lock (_gate)
        {
            for (int offset = 0; offset < Capacity; offset++)
            {
                int index = (_next + offset) % Capacity;
                string? entry = _texts[index];
                int frame = _frames[index];
                if (entry == null || frame < firstFrame || frame > lastFrame)
                {
                    continue;
                }

                AppendEntryPrefix(text, count, frame, lastFrame).Append(entry);
                count++;
            }

            foreach (IFrameEventSource source in _sources)
            {
                count += source.AppendRange(text, firstFrame, lastFrame, count);
            }
        }

        return count;
    }
}
