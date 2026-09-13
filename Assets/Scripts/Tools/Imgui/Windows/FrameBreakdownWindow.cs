#nullable enable

using System.Collections.Generic;
using Fodinae.Tools.Imgui.Profiling;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

public sealed class FrameBreakdownWindow : ToolWindow
{
    private const float RefreshInterval = 0.25f;
    private const double TargetBudgetMilliseconds = 1000.0 / 60.0;

    private readonly List<FrameProbe> _gpu = FrameProbeCatalog.CreateGpuProbes();
    private readonly List<FrameProbe> _cpu = FrameProbeCatalog.CreateCpuProbes();
    private readonly List<FrameCounter> _counters = FrameProbeCatalog.CreateCounters();
    private readonly List<string> _gpuRows = [];
    private readonly List<string> _cpuRows = [];
    private readonly List<string> _counterRows = [];

    private static readonly List<(int Start, int Length, double Weight)> _stageOrder = [];
    private static readonly List<FrameProbe> _reordered = [];

    private double _gpuPeak;
    private double _cpuPeak;

    // Заголовки и признак доступности считаются вместе со строками замеров:
    // отрисовка только читает готовое и не собирает строки на каждое событие.
    private string _gpuHeader = GroupHeader("ВИДЕОКАРТА", TargetBudgetMilliseconds);
    private string _cpuHeader = GroupHeader("ПРОЦЕССОР", TargetBudgetMilliseconds);
    private bool _anyAvailable;
    private bool _started;
    private float _nextUpdate;
    private Vector2 _scroll;

    public FrameBreakdownWindow()
        : base("Разбор кадра", new Rect(628f, 596f, 430f, 460f))
    {
    }

    public override bool WantsSampling => Visible;

    public override Vector2 MinimumSize => new(380f, 300f);

    protected override void OnVisibilityChanged(bool visible)
    {
        if (visible)
        {
            StartAll();
            return;
        }

        StopAll();
    }

    public override void Tick()
    {
        if (!Visible)
        {
            return;
        }

        if (!_started)
        {
            StartAll();
        }

        if (Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        SampleAll();
        Rebuild();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        StopAll();
        _gpuRows.Clear();
        _cpuRows.Clear();
        _counterRows.Clear();
        _gpuPeak = 0d;
        _cpuPeak = 0d;
        _gpuHeader = GroupHeader("ВИДЕОКАРТА", TargetBudgetMilliseconds);
        _cpuHeader = GroupHeader("ПРОЦЕССОР", TargetBudgetMilliseconds);
        _anyAvailable = false;
    }

    protected override void OnDispose()
    {
        StopAll();
        foreach (FrameProbe probe in _gpu)
        {
            probe.Dispose();
        }

        foreach (FrameProbe probe in _cpu)
        {
            probe.Dispose();
        }

        foreach (FrameCounter counter in _counters)
        {
            counter.Dispose();
        }
    }

    private void StartAll()
    {
        foreach (FrameProbe probe in _gpu)
        {
            probe.Start();
        }

        foreach (FrameProbe probe in _cpu)
        {
            probe.Start();
        }

        foreach (FrameCounter counter in _counters)
        {
            counter.Start();
        }

        _started = true;
    }

    private void StopAll()
    {
        foreach (FrameProbe probe in _gpu)
        {
            probe.Stop();
        }

        foreach (FrameProbe probe in _cpu)
        {
            probe.Stop();
        }

        foreach (FrameCounter counter in _counters)
        {
            counter.Stop();
        }

        _started = false;
    }

    private void SampleAll()
    {
        foreach (FrameProbe probe in _gpu)
        {
            probe.Sample();
        }

        foreach (FrameProbe probe in _cpu)
        {
            probe.Sample();
        }

        foreach (FrameCounter counter in _counters)
        {
            counter.Sample();
        }
    }

    private void Rebuild()
    {
        _gpuPeak = BuildGroup(_gpu, _gpuRows);
        _cpuPeak = BuildGroup(_cpu, _cpuRows);
        _anyAvailable = AnyAvailable();
        _gpuHeader = GroupHeader("ВИДЕОКАРТА", System.Math.Max(_gpuPeak, TargetBudgetMilliseconds));
        _cpuHeader = GroupHeader("ПРОЦЕССОР", System.Math.Max(_cpuPeak, TargetBudgetMilliseconds));

        _counterRows.Clear();
        foreach (FrameCounter counter in _counters)
        {
            _counterRows.Add(counter.Available
                ? $"{counter.Title}: {FormatCounter(counter)}"
                : $"{counter.Title}: счётчик недоступен");
        }
    }

    private static string GroupHeader(string title, double scale) =>
        $"{title} (шкала {scale:F1} мс, бюджет {TargetBudgetMilliseconds:F1} мс)";

    private static double BuildGroup(List<FrameProbe> probes, List<string> rows)
    {
        SortByStage(probes);
        rows.Clear();
        double peak = 0d;
        foreach (FrameProbe probe in probes)
        {
            if (!probe.Available)
            {
                rows.Add($"{probe.Title}  —  участок не найден");
                continue;
            }

            peak = System.Math.Max(peak, probe.AverageMilliseconds);
            rows.Add(
                $"{probe.Title}   {probe.AverageMilliseconds:F2} мс  " +
                $"(последний {probe.LastMilliseconds:F2})");
        }

        return peak;
    }

    private static void SortByStage(List<FrameProbe> probes)
    {
        _stageOrder.Clear();
        for (int i = 0; i < probes.Count; i++)
        {
            if (probes[i].IsDetail)
            {
                continue;
            }

            int end = i + 1;
            double weight = probes[i].Available ? probes[i].AverageMilliseconds : 0d;
            while (end < probes.Count && probes[end].IsDetail)
            {
                if (!probes[i].Available && probes[end].Available)
                {
                    weight = System.Math.Max(weight, probes[end].AverageMilliseconds);
                }

                end++;
            }

            _stageOrder.Add((i, end - i, weight));
        }

        _stageOrder.Sort(static (left, right) => right.Weight.CompareTo(left.Weight));

        _reordered.Clear();
        foreach ((int start, int length, double _) in _stageOrder)
        {
            for (int i = start; i < start + length; i++)
            {
                _reordered.Add(probes[i]);
            }
        }

        // Хвост без этапа над собой возможен только при ошибке в перечне;
        // терять строки молча нельзя, поэтому они дописываются как есть.
        if (_reordered.Count != probes.Count)
        {
            foreach (FrameProbe probe in probes)
            {
                if (!_reordered.Contains(probe))
                {
                    _reordered.Add(probe);
                }
            }
        }

        probes.Clear();
        probes.AddRange(_reordered);
    }

    private static string FormatCounter(FrameCounter counter) =>
        counter.IsBytes
            ? $"{counter.LastValue / (1024.0 * 1024.0):F1} МБ"
            : counter.LastValue.ToString("N0");

    protected override void DrawContent()
    {
        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;

            if (!_anyAvailable)
            {
                ToolChrome.Banner("СЧЁТЧИКИ НЕДОСТУПНЫ", ToolTheme.Warning);
                GUILayout.Label(
                    "В этой сборке не определён ENABLE_PROFILER. Он есть в вариантах " +
                    "Instrumented, Checked и Debug, но не в Release.",
                    MutedLabelStyle);
                return;
            }

            double gpuScale = System.Math.Max(_gpuPeak, TargetBudgetMilliseconds);
            double cpuScale = System.Math.Max(_cpuPeak, TargetBudgetMilliseconds);

            ToolChrome.SectionHeader(_gpuHeader);
            DrawGroup(_gpuRows, _gpu, gpuScale, ToolTheme.FrameGraphColor);

            ToolChrome.SectionHeader(_cpuHeader);
            DrawGroup(_cpuRows, _cpu, cpuScale, ToolTheme.Warning);

            ToolChrome.SectionHeader("СЧЁТЧИКИ КАДРА");
            foreach (string row in _counterRows)
            {
                GUILayout.Label(row, MutedLabelStyle);
            }
        }
    }

    private static void DrawGroup(List<string> rows, List<FrameProbe> probes, double peak, Color color)
    {
        if (rows.Count == 0)
        {
            GUILayout.Label("Замеров ещё нет.", MutedLabelStyle);
            return;
        }

        for (int i = 0; i < rows.Count && i < probes.Count; i++)
        {
            GUILayout.Label(rows[i], MutedLabelStyle);
            if (!probes[i].Available)
            {
                continue;
            }

            float share = peak > 0d ? (float)(probes[i].AverageMilliseconds / peak) : 0f;
            ToolChrome.MeterLine(share, color, 3f);
            GUILayout.Space(2f);
        }
    }

    private bool AnyAvailable()
    {
        foreach (FrameProbe probe in _gpu)
        {
            if (probe.Available)
            {
                return true;
            }
        }

        foreach (FrameProbe probe in _cpu)
        {
            if (probe.Available)
            {
                return true;
            }
        }

        return false;
    }
}
