#nullable enable

using System.Text;

namespace Kern.Core.Interfaces.Diagnostics;

/// <summary>
/// Подсистема, которая держит разбор своих дорогих кадров и отдаёт его отчёту
/// о провисе только по запросу.
/// </summary>
///
/// Строка в <see cref="FrameEventLog"/> форматируется в момент записи. Для
/// редких событий это дёшево, а для разбора стадий, который может понадобиться
/// в любом кадре, — нет: террейн, дорогой каждый кадр, собирал бы длинную
/// строку каждый кадр и сам давал бы мусор и провисы. Источник хранит кадры
/// структурами и пишет текст, только когда провис уже случился.
public interface IFrameEventSource
{
    /// <summary>
    /// Дописать свои записи кадров [firstFrame, lastFrame] через
    /// <see cref="FrameEventLog.AppendEntryPrefix"/>; вернуть их число.
    /// </summary>
    int AppendRange(StringBuilder text, int firstFrame, int lastFrame, int writtenBefore);
}
