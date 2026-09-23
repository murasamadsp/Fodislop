#nullable enable

using System.Text;
using UnityEngine;

namespace Kern.Core.Interfaces.Diagnostics;

/// <summary>
/// Журнал редких тяжёлых событий кадра: пересоздание текстур, новый атлас,
/// новые материалы, декодирование, пересоздание ресурсов света.
/// </summary>
///
/// Провис, в котором главный поток ждёт поток рендера, не виден маркерам
/// скриптов: работу отдали рендеру кадром-двумя раньше, и в провисшем кадре
/// скрипты почти ничего не стоят. Такие события редки и сами по себе дёшевы
/// на главном потоке, но тяжелы для рендера — поэтому их и записываем: отчёт
/// о провисе (FrameStall) печатает всё, что случилось за несколько кадров до
/// него.
public static class FrameEventLog
{
    private const int Capacity = 64;

    private static readonly int[] _frames = new int[Capacity];
    private static readonly string?[] _texts = new string?[Capacity];
    private static readonly object _gate = new();
    private static int _next;

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

    /// <summary>Дописать события кадров [firstFrame, lastFrame]; вернуть их число.</summary>
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

                text.Append(count == 0 ? " · события: " : "; ")
                    .Append('[').Append(frame - lastFrame).Append("] ")
                    .Append(entry);
                count++;
            }
        }

        return count;
    }
}
