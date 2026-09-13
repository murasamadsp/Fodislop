#nullable enable

using System;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

public sealed class FrameCounter : IDisposable
{
    private readonly ProfilerCategory _category;
    private readonly string[] _counterNames;
    private ProfilerRecorder _recorder;

    public FrameCounter(
        string title,
        string counterName,
        ProfilerCategory category,
        bool isBytes = false,
        params string[] alternativeNames)
    {
        Title = title;
        CounterName = counterName;
        _category = category;
        IsBytes = isBytes;
        _counterNames = alternativeNames.Length == 0 ? [counterName] : [counterName, ..alternativeNames];
    }

    public string Title { get; }

    public string CounterName { get; }

    public bool IsBytes { get; }

    public bool Available => _recorder.Valid;

    public long LastValue { get; private set; }

    public void Start()
    {
        if (_recorder.Valid)
        {
            return;
        }

        foreach (string name in _counterNames)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                _category,
                name,
                1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (recorder.Valid)
            {
                _recorder = recorder;
                return;
            }

            recorder.Dispose();
        }

        foreach (string name in _counterNames)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(_category, name);
            if (recorder.Valid)
            {
                _recorder = recorder;
                return;
            }

            recorder.Dispose();
        }
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
        if (!_recorder.Valid)
        {
            Start();
        }

        if (_recorder.Valid)
        {
            LastValue = _recorder.LastValue;
        }

#if UNITY_EDITOR
        if (LastValue == 0 && CounterName == "Draw Calls Count")
        {
            LastValue = UnityEditor.UnityStats.drawCalls;
        }
#endif
    }

    public void Dispose()
    {
        Stop();
    }
}
