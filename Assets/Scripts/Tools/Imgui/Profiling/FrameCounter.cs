#nullable enable

using System;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

/// <summary>
/// Встроенный счётчик кадра: вызовы отрисовки, треугольники, память текстур.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="FrameProbe"/>, потому что величина другой природы.
/// Участок кадра измеряется временем и усредняется по окну; счётчик — это
/// количество за кадр, и усреднять его незачем: если вызовов отрисовки стало
/// вдвое больше, это видно по последнему кадру, а среднее только размажет
/// момент, когда это случилось.
///
/// Имена счётчиков задаёт Unity, и набор зависит от версии и от платформы.
/// Отсутствующий счётчик показывается как отсутствующий, а не как ноль.
/// </remarks>
public sealed class FrameCounter : IDisposable
{
    private readonly ProfilerCategory _category;
    private ProfilerRecorder _recorder;

    public FrameCounter(string title, string counterName, ProfilerCategory category, bool isBytes = false)
    {
        Title = title;
        CounterName = counterName;
        _category = category;
        IsBytes = isBytes;
    }

    public string Title { get; }

    public string CounterName { get; }

    /// <summary>Показывать ли значение как объём памяти, а не как число.</summary>
    public bool IsBytes { get; }

    public bool Available => _recorder.Valid;

    public long LastValue { get; private set; }

    public void Start()
    {
        if (_recorder.Valid)
        {
            return;
        }

        _recorder = ProfilerRecorder.StartNew(_category, CounterName);
    }

    public void Stop()
    {
        if (_recorder.Valid)
        {
            _recorder.Dispose();
        }

        _recorder = default;
        LastValue = 0;
    }

    public void Sample()
    {
        if (_recorder.Valid)
        {
            LastValue = _recorder.LastValue;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
