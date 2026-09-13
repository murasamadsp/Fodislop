#nullable enable

using System;
using Unity.Profiling;

namespace Fodinae.Tools.Imgui.Profiling;

public sealed class FrameProbe : IDisposable
{
    private const int SampleCapacity = 20;

    private static readonly ProfilerCategory[] _Candidates =
    [
        ProfilerCategory.Render,
        ProfilerCategory.Scripts,
        ProfilerCategory.Gui,
        ProfilerCategory.Internal,
        ProfilerCategory.Memory,
        ProfilerCategory.Audio,
        ProfilerCategory.Physics,
        ProfilerCategory.Input,
    ];

    private readonly ProfilerCategory? _explicitCategory;
    private ProfilerRecorder _recorder;

    public FrameProbe(
        string title,
        string markerName,
        bool isDetail = false,
        ProfilerCategory? category = null)
    {
        Title = title;
        MarkerName = markerName;
        IsDetail = isDetail;
        _explicitCategory = category;
    }

    public string Title { get; }

    public bool IsDetail { get; }

    public string MarkerName { get; }

    public bool Available => _recorder.Valid;

    public double LastMilliseconds { get; private set; }

    public double AverageMilliseconds { get; private set; }

    public void Start()
    {
        if (_recorder.Valid)
        {
            return;
        }

        if (_explicitCategory.HasValue)
        {
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                _explicitCategory.Value,
                MarkerName,
                SampleCapacity,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (recorder.Valid)
            {
                _recorder = recorder;
                return;
            }

            recorder.Dispose();
        }

        foreach (ProfilerCategory category in _Candidates)
        {
            if (_explicitCategory.HasValue && category == _explicitCategory.Value)
            {
                continue;
            }

            ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                category,
                MarkerName,
                SampleCapacity,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
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
        LastMilliseconds = 0d;
        AverageMilliseconds = 0d;
    }

    public void Sample()
    {
        if (!_recorder.Valid)
        {
            Start();
            if (!_recorder.Valid)
            {
                return;
            }
        }

        LastMilliseconds = _recorder.LastValue / 1_000_000.0;

        int count = _recorder.Count;
        if (count <= 0)
        {
            AverageMilliseconds = LastMilliseconds;
            return;
        }

        double total = 0d;
        for (int i = 0; i < count; i++)
        {
            total += _recorder.GetSample(i).Value;
        }

        AverageMilliseconds = total / count / 1_000_000.0;
    }

    public void Dispose()
    {
        Stop();
    }
}
