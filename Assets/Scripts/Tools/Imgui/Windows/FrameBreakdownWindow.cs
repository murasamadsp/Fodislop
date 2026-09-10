#nullable enable

using System.Collections.Generic;
using Fodinae.Tools.Imgui.Profiling;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

/// <summary>
/// Разбор кадра по этапам: где именно уходит время.
/// </summary>
/// <remarks>
/// Окно, которого не хватало. Разметка стояла в коде давно, но не читалась
/// ничем, и вопрос «почему при выключенном освещении кадр всё равно
/// одиннадцать миллисекунд» решался чтением исходников и догадками. Теперь
/// этапы названы, измерены и отсортированы по цене.
///
/// ПОЧЕМУ СОРТИРОВКА ПО ЦЕНЕ. Список в порядке объявления читается ровно один
/// раз — когда его пишут. Дальше нужен один ответ: что сейчас самое дорогое.
/// Порядок строк меняется, и это не недостаток, а сам смысл: строка наверху и
/// есть ответ.
///
/// ПОЧЕМУ ЧАСТИ С ТОЧКОЙ. Вложенные участки помечены точкой в начале. Сумма
/// частей, заметно меньшая, чем обёртка вокруг них, — это работа, которую
/// никто не разметил, и место, куда стоит смотреть следующим.
/// </remarks>
public sealed class FrameBreakdownWindow : ToolWindow
{
    private const float RefreshInterval = 0.25f;

    private readonly List<FrameProbe> _gpu = FrameProbeCatalog.CreateGpuProbes();
    private readonly List<FrameProbe> _cpu = FrameProbeCatalog.CreateCpuProbes();
    private readonly List<FrameCounter> _counters = FrameProbeCatalog.CreateCounters();
    private readonly List<string> _gpuRows = [];
    private readonly List<string> _cpuRows = [];
    private readonly List<string> _counterRows = [];

    /// <summary>Черновики перестановки. Общие и переиспользуемые: окно рисуется часто.</summary>
    private static readonly List<(int Start, int Length, double Weight)> _stageOrder = [];
    private static readonly List<FrameProbe> _reordered = [];

    private double _gpuPeak;
    private double _cpuPeak;
    private bool _started;
    private float _nextUpdate;
    private Vector2 _scroll;

    public FrameBreakdownWindow()
        : base("Разбор кадра", new Rect(628f, 596f, 430f, 460f))
    {
    }

    /// <summary>
    /// Счётчики держатся открытыми только пока окно открыто.
    /// </summary>
    /// <remarks>
    /// Каждый <c>ProfilerRecorder</c> — это включённый сбор в рантайме, и три
    /// десятка одновременно стоят кадру заметных денег. Инструмент, который
    /// платит за себя, когда на него не смотрят, — это тот самый случай, когда
    /// измерение меняет измеряемое.
    /// </remarks>
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

        _counterRows.Clear();
        foreach (FrameCounter counter in _counters)
        {
            _counterRows.Add(counter.Available
                ? $"{counter.Title}: {FormatCounter(counter)}"
                : $"{counter.Title}: счётчик недоступен");
        }
    }

    /// <summary>
    /// Упорядочивает группу по цене и собирает подписи. Возвращает максимум.
    /// </summary>
    /// <remarks>
    /// Порядок наводится по этапам целиком, а не по отдельным строкам. Части
    /// остаются под своим этапом в том порядке, в каком объявлены, — иначе
    /// сортировка растащила бы их по всему списку и уничтожила ровно то, ради
    /// чего он так и составлен: сравнение суммы частей с обёрткой вокруг них.
    /// Первая версия делала именно это, и получалось не «разбор кадра», а
    /// прыгающий столбец чисел без структуры.
    /// </remarks>
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

    /// <summary>
    /// Переставляет этапы по убыванию цены, не разлучая их с частями.
    /// </summary>
    /// <remarks>
    /// Цена этапа берётся из его собственного замера, а не из суммы частей:
    /// обёртка меряет и неразмеченную работу тоже, и именно она — настоящая
    /// цена этапа. У этапа без замера ценой служит самая дорогая его часть,
    /// иначе пропавший маркер утащил бы весь блок в конец списка.
    /// </remarks>
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

            if (!AnyAvailable())
            {
                ToolChrome.Banner("СЧЁТЧИКИ НЕДОСТУПНЫ", ToolTheme.Warning);
                GUILayout.Label(
                    "В этой сборке не определён ENABLE_PROFILER. Он есть в вариантах " +
                    "Instrumented, Checked и Debug, но не в Release.",
                    MutedLabelStyle);
                return;
            }

            const double targetBudget = 1000.0 / 60.0;
            double gpuScale = System.Math.Max(_gpuPeak, targetBudget);
            double cpuScale = System.Math.Max(_cpuPeak, targetBudget);

            ToolChrome.SectionHeader($"ВИДЕОКАРТА (шкала {gpuScale:F1} мс, бюджет {targetBudget:F1} мс)");
            DrawGroup(_gpuRows, _gpu, gpuScale, ToolTheme.FrameGraphColor);

            ToolChrome.SectionHeader($"ПРОЦЕССОР (шкала {cpuScale:F1} мс, бюджет {targetBudget:F1} мс)");
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
