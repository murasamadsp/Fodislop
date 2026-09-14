#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Fodinae.Tools.Imgui.Profiling;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

public sealed class FrameBreakdownWindow : ToolWindow
{
    private const float RefreshInterval = 0.25f;
    private const double BudgetMilliseconds = 1000.0 / 60.0;
    private const int FrameTabLoopRows = 8;
    private const int MaxDiscoveredMarkers = 80;
    private const string OwnMarkerPrefix = "Fodinae.";

    private enum Tab
    {
        Frame,
        Loop,
        Hot,
        Spikes,
        Layout,
        Stages,
        Tools,
        Memory,
        Search,
        Scene,
    }

    private static readonly (Tab Tab, string Label)[] _Tabs =
    [
        (Tab.Frame, "Кадр"),
        (Tab.Loop, "Цикл"),
        (Tab.Hot, "Горячее"),
        (Tab.Spikes, "Всплески"),
        (Tab.Layout, "Раскладка"),
        (Tab.Stages, "Участки"),
        (Tab.Tools, "Окна"),
        (Tab.Memory, "Память"),
        (Tab.Search, "Поиск"),
        (Tab.Scene, "Сцена"),
    ];

    private enum RowKind
    {
        Header,
        Text,
        Warning,
    }

    private readonly record struct Row(RowKind Kind, string Text, float Share = -1f, Color Color = default, string? Pin = null);

    private readonly ThreadTimings _timings = new();
    private readonly PlayerLoopBreakdown _loop = new();
    private readonly MarkerSearch _search = new();
    private readonly HotMarkerSweep _sweep = new();
    private readonly SpikeSweep _spikes = new();
    private bool _spikesRequested;
    private bool _layoutActive;
    private readonly UiLayoutTracker _layout = new();
    private readonly SceneCensus _census = new();
    private bool _sweepRequested;
    private bool _layoutResetRequested;
    private bool _layoutTodoRequested;
    private readonly FrameProbe _playerLoop = new("Игровой цикл (PlayerLoop)", "PlayerLoop");
    private readonly List<FrameProbe> _cpu = FrameProbeCatalog.CreateCpuProbes();
    private readonly List<FrameProbe> _gpu = FrameProbeCatalog.CreateGpuProbes();
    private readonly List<FrameProbe> _gpuRecord = FrameProbeCatalog.CreateGpuRecordProbes();
    private readonly List<FrameProbe> _interface = FrameProbeCatalog.CreateInterfaceProbes();
    private readonly List<FrameProbe> _memory = FrameProbeCatalog.CreateMemoryProbes();
    private readonly List<FrameProbe> _render = FrameProbeCatalog.CreateRenderCounters();
    private readonly List<FrameProbe> _discovered = [];
    private readonly HashSet<string> _catalogNames = new(StringComparer.Ordinal);
    private readonly List<FrameProbe> _reordered = [];
    private readonly List<(int Start, int Length, double Weight)> _stageOrder = [];
    private readonly List<ToolWindow> _toolWindows = [];

    private readonly Dictionary<Tab, List<Row>> _rows = new()
    {
        [Tab.Frame] = [],
        [Tab.Loop] = [],
        [Tab.Hot] = [],
        [Tab.Spikes] = [],
        [Tab.Layout] = [],
        [Tab.Stages] = [],
        [Tab.Tools] = [],
        [Tab.Memory] = [],
        [Tab.Search] = [],
        [Tab.Scene] = [],
    };

    private Tab _tab = Tab.Frame;
    private Tab _requestedTab = Tab.Frame;
    private bool _started;
    private bool _profilerAvailable;
    private bool _copyRequested;
    private float _copiedUntil;
    private float _nextUpdate;
    private int _discoveredVersion = -1;
    private Vector2 _scroll;

    public FrameBreakdownWindow()
        : base("Разбор кадра", new Rect(628f, 596f, 470f, 560f))
    {
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                _catalogNames.Add(probe.MarkerName);
            }
        }
    }

    public override bool WantsSampling => Visible;

    public override Vector2 MinimumSize => new(400f, 320f);

    private IEnumerable<List<FrameProbe>> AllCatalogLists()
    {
        yield return _cpu;
        yield return _gpu;
        yield return _gpuRecord;
        yield return _interface;
        yield return _memory;
        yield return _render;
    }

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

        // Тайминги потоков движок отдаёт только за последние кадры, поэтому
        // снимаются каждый кадр, а не с частотой обновления строк.
        _timings.Tick();
        foreach (FrameProbe probe in _gpu)
        {
            probe.TickGpu();
        }

        _search.Tick(_tab == Tab.Search);
        // Копирование отчёта тоже снимает перепись: иначе в отчёте, снятом
        // с другой вкладки, раздел «Сцена» оставался бы пустым.
        _census.Tick(_tab == Tab.Scene || _copyRequested);
        if (_sweepRequested)
        {
            _sweepRequested = false;
            _sweep.Begin();
        }

        _sweep.Tick();

        // Два прохода сразу мешали бы друг другу: сотни лишних рекордеров
        // сами дают всплески при открытии пачек.
        if (_spikesRequested && !_sweep.Running)
        {
            _spikesRequested = false;
            _spikes.Begin();
        }

        _spikes.Tick();
        if (_layoutResetRequested)
        {
            _layoutResetRequested = false;
            _layout.ResetStats();
        }

        // Трекер раз в секунду ищет все UIDocument и обходит всё дерево
        // интерфейса, вместе с подписями мира: сам давал рывок каждую
        // секунду. Он работает, только пока открыта его вкладка.
        bool layoutWanted = _tab == Tab.Layout;
        if (layoutWanted != _layoutActive)
        {
            _layoutActive = layoutWanted;
            if (layoutWanted)
            {
                _layout.Start();
            }
            else
            {
                _layout.Stop();
            }
        }

        _layout.Tick();
        if (_layoutTodoRequested)
        {
            _layoutTodoRequested = false;
            string? todo = _layout.WriteTodo();
            if (todo != null)
            {
                Debug.Log($"[FrameBreakdown] TODO раскладки: {todo}");
            }
        }

        // Отчёт без прохода по всем маркерам бесполезен для вопроса «что
        // именно рендерится»: копирование сначала прочёсывает, потом копирует.
        if (_copyRequested && !_sweep.HasResults && !_sweep.Running)
        {
            _sweep.Begin();
        }

        if (_copyRequested && !_sweep.Running)
        {
            _copyRequested = false;
            GUIUtility.systemCopyBuffer = BuildReport();
            _copiedUntil = Time.unscaledTime + 2f;
        }

        UpdateButtonLabels();
        if (Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        SampleAll();
        RebuildRows();
    }

    // GPU-тайминги приходят с задержкой. Без связи по идентификатору кадра
    // нельзя группировать их по CPU-маркеру текущего или соседнего кадра.

    // Подписи кнопок собираются в Tick и только при смене чисел: в DrawContent
    // интерполяция шла на каждое событие IMGUI.
    private string _copyLabel = "Копировать отчёт";
    private string _sweepLabel = "Прочесать все маркеры";
    private int _labelProcessed = -1;
    private int _labelState = -1;

    private void UpdateButtonLabels()
    {
        int state = _copyRequested ? 1 : _sweep.Running ? 2 : Time.unscaledTime < _copiedUntil ? 3 : 0;
        if (state == _labelState && (state is 0 or 3 || _sweep.Processed == _labelProcessed))
        {
            return;
        }

        _labelState = state;
        _labelProcessed = _sweep.Processed;
        _copyLabel = state switch
        {
            1 => $"Прочёсываю: {_sweep.Processed} / {_sweep.Total}",
            3 => "Скопировано",
            _ => "Копировать отчёт",
        };
        _sweepLabel = _sweep.Running
            ? $"Идёт проход: {_sweep.Processed} / {_sweep.Total}"
            : "Прочесать все маркеры";
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        StopAll();
        _timings.Clear();
        foreach (List<Row> rows in _rows.Values)
        {
            rows.Clear();
        }
    }

    protected override void OnDispose()
    {
        StopAll();
        _search.Dispose();
        _sweep.Dispose();
        _spikes.Dispose();
        _layout.Dispose();
        _loop.Dispose();
    }

    private void StartAll()
    {
        _timings.Clear();
        _playerLoop.Start();
        _loop.Begin();
        Fodinae.Core.Interfaces.Diagnostics.AllocationLedger.Enabled = true;
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Start();
            }
        }

        _started = true;
    }

    private void StopAll()
    {
        _playerLoop.Stop();
        _loop.Stop();
        _search.Stop();
        _sweep.Cancel();
        _spikes.Cancel();
        _layout.Stop();
        _layoutActive = false;
        Fodinae.Core.Interfaces.Diagnostics.AllocationLedger.Enabled = false;
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Stop();
            }
        }

        foreach (FrameProbe probe in _discovered)
        {
            probe.Dispose();
        }

        _discovered.Clear();
        _discoveredVersion = -1;
        _started = false;
    }

    private void SampleAll()
    {
        _playerLoop.Sample();
        _loop.Sample();
        _search.Sample();
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Sample();
            }
        }

        DiscoverOwnMarkers();
        foreach (FrameProbe probe in _discovered)
        {
            probe.Sample();
        }

        _discovered.Sort(static (left, right) => right.Average.CompareTo(left.Average));
        _profilerAvailable = _playerLoop.Available || MarkerDirectory.All.Count > 0;
    }

    // Наши маркеры, которых нет в перечне: новый участок виден сразу, без
    // правки каталога. Окна инструментов показываются на своей вкладке.
    private void DiscoverOwnMarkers()
    {
        if (_discoveredVersion == MarkerDirectory.Version)
        {
            return;
        }

        _discoveredVersion = MarkerDirectory.Version;
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (FrameProbe probe in _discovered)
        {
            known.Add(probe.MarkerName);
        }

        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (_discovered.Count >= MaxDiscoveredMarkers)
            {
                break;
            }

            if (!info.Name.StartsWith(OwnMarkerPrefix, StringComparison.Ordinal) ||
                info.Name.StartsWith("Fodinae.Tools.", StringComparison.Ordinal) ||
                // Маркеры Test Runner — имена тестов, а не участки кадра.
                info.Name.StartsWith("Fodinae.Tests.", StringComparison.Ordinal) ||
                _catalogNames.Contains(info.Name) ||
                known.Contains(info.Name) ||
                info.Unit != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                continue;
            }

            var probe = new FrameProbe(info, "· " + info.Name.Substring(OwnMarkerPrefix.Length));
            probe.Start();
            _discovered.Add(probe);
        }
    }

    // Строки собираются только для открытой вкладки: остальные никто не
    // видит, а их сборка каждые 250 мс была главным мусором самого окна.
    private void RebuildRows()
    {
        List<Row> rows = _rows[_tab];
        switch (_tab)
        {
            case Tab.Frame: BuildFrameRows(rows); break;
            case Tab.Loop: BuildLoopRows(rows); break;
            case Tab.Hot: BuildHotRows(rows); break;
            case Tab.Spikes: BuildSpikeRows(rows); break;
            case Tab.Layout: BuildLayoutRows(rows); break;
            case Tab.Stages: BuildStageRows(rows); break;
            case Tab.Tools: BuildToolRows(rows); break;
            case Tab.Memory: BuildMemoryRows(rows); break;
            case Tab.Search: BuildSearchRows(rows); break;
            case Tab.Scene: BuildSceneRows(rows); break;
        }
    }

    private void RebuildAllRows()
    {
        BuildFrameRows(_rows[Tab.Frame]);
        BuildLoopRows(_rows[Tab.Loop]);
        BuildHotRows(_rows[Tab.Hot]);
        BuildSpikeRows(_rows[Tab.Spikes]);
        BuildLayoutRows(_rows[Tab.Layout]);
        BuildStageRows(_rows[Tab.Stages]);
        BuildToolRows(_rows[Tab.Tools]);
        BuildMemoryRows(_rows[Tab.Memory]);
        BuildSearchRows(_rows[Tab.Search]);
        BuildSceneRows(_rows[Tab.Scene]);
    }

    private void BuildFrameRows(List<Row> rows)
    {
        rows.Clear();
        rows.Add(new Row(RowKind.Header, "ПОТОКИ — АГРЕГАТЫ FRAMETIMINGMANAGER"));
        if (!_timings.Available)
        {
            rows.Add(new Row(
                RowKind.Warning,
                "Тайминги потоков недоступны. В сборке нужен флаг Frame Timing Stats " +
                "в Player Settings; в редакторе они есть не на всех платформах."));
        }
        else
        {
            double scale = Math.Max(BudgetMilliseconds, _timings.Frame.Peak);
            AddSeries(rows, "Кадр целиком", _timings.Frame, scale, ToolTheme.Accent);
            AddSeries(rows, "· главный поток — активная работа", _timings.MainThread, scale, ToolTheme.Warning);
            AddSeries(rows, "· ожидание вывода — отдельная метрика", _timings.PresentWait, scale, ToolTheme.Warning);
            AddSeries(rows, "· поток рендера", _timings.RenderThread, scale, ToolTheme.FrameGraphColor);
            if (_timings.GpuSampledFrames > 0)
            {
                AddSeries(rows, "· видеокарта — последние доступные замеры", _timings.Gpu, scale, ToolTheme.FrameGraphColor);
                rows.Add(new Row(RowKind.Text,
                    $"GPU-замеров {_timings.GpuSampledFrames} из {_timings.SampledFrames} уникальных кадров за сеанс окна; " +
                    "пропуски не считаются нулевой стоимостью."));
            }
            else
            {
                rows.Add(new Row(RowKind.Text, "· видеокарта — валидные замеры пока не получены"));
            }

            rows.Add(new Row(RowKind.Text,
                "Причина длительности кадра по этим агрегатам не установлена. " +
                "Нужна временная шкала одного кадра с потоками и GPU."));
        }

        rows.Add(new Row(RowKind.Header, "ИГРОВОЙ ЦИКЛ"));
        if (!_playerLoop.Available)
        {
            rows.Add(new Row(RowKind.Warning, "Маркер PlayerLoop не найден."));
        }
        else
        {
            double scale = Math.Max(BudgetMilliseconds, _playerLoop.Peak);
            AddProbe(rows, _playerLoop, scale, ToolTheme.Warning);
            rows.Add(new Row(
                RowKind.Text,
                $"· сумма найденных маркеров систем   {_loop.AccountedMilliseconds:F2} мс",
                (float)(_loop.AccountedMilliseconds / scale),
                ToolTheme.Warning));
            rows.Add(new Row(
                RowKind.Text,
                "Разность с PlayerLoop не определяет стоимость редактора: " +
                "покрытие маркерами неполное, окна выборок могут различаться."));

            int shown = Math.Min(FrameTabLoopRows, _loop.Sorted.Count);
            for (int i = 0; i < shown; i++)
            {
                AddProbe(rows, _loop.Sorted[i], scale, ToolTheme.Warning, "· " + _loop.Sorted[i].Title, pinnable: true);
            }
        }

        if (_search.Pins.Count > 0)
        {
            rows.Add(new Row(RowKind.Header, "ЗАКРЕПЛЁННЫЕ"));
            AddProbeList(rows, _search.Pins, ToolTheme.Accent, pinnable: true);
        }

        rows.Add(new Row(RowKind.Header, "МУСОР"));
        AddCounter(rows, _memory[0]);
        AddCounter(rows, _memory[1]);
        long toolBytes = ToolAllocatedBytes();
        rows.Add(new Row(RowKind.Text, $"· отдельный замер окон: {toolBytes / 1024d:F1} КБ (главный поток)"));
        AddAllocationLedgerRows(rows);
    }

    private void BuildLoopRows(List<Row> rows)
    {
        rows.Clear();
        rows.Add(new Row(
            RowKind.Header,
            $"СИСТЕМЫ PLAYERLOOP ({_loop.Sorted.Count} из {_loop.SystemCount})"));
        if (_loop.MissingCount > 0)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"Без маркера в этом сеансе: {_loop.MissingCount} (ещё не срабатывали или не размечены)."));
        }

        double scale = Math.Max(BudgetMilliseconds, _playerLoop.Available ? _playerLoop.Peak : 0d);
        AddProbeList(rows, _loop.Sorted, ToolTheme.Warning, pinnable: true, scale);
    }

    private void BuildLayoutRows(List<Row> rows)
    {
        const int keep = 40;
        _layout.Rebuild(keep);
        rows.Clear();
        rows.Add(new Row(RowKind.Header, "РАСКЛАДКА ИНТЕРФЕЙСА ИГРЫ"));
        if (!_layout.LayoutMarkerAvailable)
        {
            rows.Add(new Row(RowKind.Warning, "Маркер PanelSettings.ValidateLayout ещё не сработал — пики не определить."));
        }

        rows.Add(new Row(
            RowKind.Text,
            $"Раскладка сейчас {_layout.LastLayoutMilliseconds:F2} мс, пик {_layout.PeakLayoutMilliseconds:F2} мс",
            (float)(_layout.LastLayoutMilliseconds / Math.Max(BudgetMilliseconds, _layout.PeakLayoutMilliseconds)),
            ToolTheme.Error));
        rows.Add(new Row(
            RowKind.Text,
            $"Кадров {_layout.Frames}, из них с раскладкой ≥ {UiLayoutTracker.SpikeMilliseconds:F0} мс: {_layout.SpikeFrames}"));
        rows.Add(new Row(
            RowKind.Text,
            $"Элементов под наблюдением {_layout.ElementCount}; сдвинулось в последнем кадре {_layout.ChangedInLastFrame}, " +
            $"максимум за кадр {_layout.PeakChangedInFrame}, всего изменений {_layout.TotalChanges}"));

        rows.Add(new Row(RowKind.Text, $"Журнал каждой перераскладки: {_layout.LogPath ?? "не открыт"}"));
        rows.Add(new Row(
            RowKind.Text,
            _layout.LastTodoPath != null
                ? $"TODO: {_layout.LastTodoPath} (пишется и при закрытии окна)"
                : "TODO запишется кнопкой или при закрытии окна."));

        AddUiCompositionRows(rows);

        rows.Add(new Row(RowKind.Header, "КТО ДВИГАЕТСЯ — СНАЧАЛА В ПИКОВЫХ КАДРАХ"));
        if (_layout.Sorted.Count == 0)
        {
            rows.Add(new Row(RowKind.Text, "Ни один элемент не менял геометрию с начала наблюдения."));
            return;
        }

        int top = Math.Max(1, _layout.Sorted[0].Changes);
        foreach (UiLayoutTracker.Stat stat in _layout.Sorted)
        {
            double perFrame = _layout.Frames > 0 ? (double)stat.Changes / _layout.Frames : 0d;
            rows.Add(new Row(
                RowKind.Text,
                $"{stat.Label}   в пиках {stat.SpikeChanges}, всего {stat.Changes} ({perFrame:P0} кадров), " +
                $"размер {stat.SizeChanges}, сейчас {stat.LastRect.width:F0}×{stat.LastRect.height:F0} @ {stat.LastRect.x:F0},{stat.LastRect.y:F0}",
                (float)stat.Changes / top,
                ToolTheme.Error));
        }
    }

    private readonly List<KeyValuePair<string, int>> _subtreeSorted = [];
    private readonly List<KeyValuePair<Texture, int>> _textureSorted = [];

    private void AddUiCompositionRows(List<Row> rows)
    {
        UiLayoutTracker.RenderStats stats = _layout.Stats;
        int atlasLimit = stats.AtlasLimit > 0 ? stats.AtlasLimit : 64;
        int bigTextures = 0;
        foreach (Texture texture in stats.Textures.Keys)
        {
            if (texture.width > atlasLimit || texture.height > atlasLimit)
            {
                bigTextures++;
            }
        }

        rows.Add(new Row(RowKind.Header, "СОСТАВ ВИДИМОГО ИНТЕРФЕЙСА (РАЗ В СЕКУНДУ)"));
        rows.Add(new Row(
            RowKind.Text,
            $"Видимых элементов {stats.Visible}; текстов {stats.Texts} ({stats.TextCharacters} символов), " +
            $"с обводкой {stats.OutlinedTexts}, с тенью {stats.ShadowedTexts}"));
        rows.Add(new Row(
            RowKind.Text,
            $"Картинок {stats.Images}, разных текстур {stats.Textures.Count}, из них крупнее {atlasLimit} px (вне атласа) {bigTextures}; " +
            "слотов текстур на пакет 8"));
        rows.Add(new Row(
            RowKind.Text,
            $"Обрезающих контейнеров {stats.Clips} (со скруглением — стенсил — {stats.RoundedClips}); " +
            $"полупрозрачных {stats.Translucent}; сдвинутых translate {stats.Translated}"));

        _subtreeSorted.Clear();
        _subtreeSorted.AddRange(stats.VisibleBySubtree);
        _subtreeSorted.Sort(static (left, right) => right.Value.CompareTo(left.Value));
        int maxSubtree = _subtreeSorted.Count > 0 ? Math.Max(1, _subtreeSorted[0].Value) : 1;
        for (int i = 0; i < _subtreeSorted.Count && i < 12; i++)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"· {_subtreeSorted[i].Key}   видимых {_subtreeSorted[i].Value}",
                (float)_subtreeSorted[i].Value / maxSubtree,
                ToolTheme.Accent));
        }

        _textureSorted.Clear();
        _textureSorted.AddRange(stats.Textures);
        _textureSorted.Sort(static (left, right) => right.Value.CompareTo(left.Value));
        for (int i = 0; i < _textureSorted.Count && i < 15; i++)
        {
            Texture texture = _textureSorted[i].Key;
            bool outsideAtlas = texture.width > atlasLimit || texture.height > atlasLimit;
            rows.Add(new Row(
                RowKind.Text,
                $"· текстура «{texture.name}» {texture.width}×{texture.height}   элементов {_textureSorted[i].Value}" +
                (outsideAtlas ? "   · вне атласа" : string.Empty)));
        }
    }

    private void BuildSpikeRows(List<Row> rows)
    {
        rows.Clear();
        if (_spikes.Running)
        {
            rows.Add(new Row(
                RowKind.Header,
                $"ЛОВЛЮ ВСПЛЕСКИ: {_spikes.Processed} ИЗ {_spikes.Total} МАРКЕРОВ"));
            rows.Add(new Row(
                RowKind.Text,
                $"Пока поймано {_spikes.SpikesSeen} просевших кадров из {_spikes.FramesSeen}. " +
                "Стойте на месте и не трогайте окна до конца прохода."));
            return;
        }

        if (!_spikes.HasResults)
        {
            rows.Add(new Row(
                RowKind.Text,
                "Кнопка «Поймать всплески» записывает все маркеры покадрово пачками дольше секунды, " +
                "находит просевшие кадры (в 1.5 раза и на 4 мс дольше медианы) и показывает, " +
                "какие маркеры в них выросли сильнее всего. Проход идёт около полуминуты."));
            return;
        }

        rows.Add(new Row(RowKind.Header, "ПРОСЕВШИЕ КАДРЫ"));
        rows.Add(new Row(
            RowKind.Text,
            $"· {_spikes.SpikesSeen} из {_spikes.FramesSeen} кадров; обычный кадр {_spikes.MedianFrameMilliseconds:F1} мс, " +
            $"просевший {_spikes.SpikeFrameMilliseconds:F1} мс (по {_spikes.FrameSource})"));
        if (_spikes.SpikesSeen == 0)
        {
            rows.Add(new Row(RowKind.Text, "Выбросов за проход не было."));
            return;
        }

        rows.Add(new Row(RowKind.Header, $"ЧТО ВЫРОСЛО В ПРОСЕВШИХ КАДРАХ ({_spikes.Hits.Count})"));
        rows.Add(new Row(
            RowKind.Text,
            "«+X мс» — насколько маркер дороже своей обычной медианы в просевшем кадре. " +
            "Родитель и ребёнок оба в списке; ищите самый глубокий с тем же приростом."));
        double scale = 1.0;
        foreach (SpikeSweep.Hit hit in _spikes.Hits)
        {
            scale = Math.Max(scale, hit.ExcessMilliseconds);
        }

        foreach (SpikeSweep.Hit hit in _spikes.Hits)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{hit.Marker.Name}   +{hit.ExcessMilliseconds:F2} мс  (обычно {hit.BaselineMilliseconds:F2}, " +
                $"кадров {hit.SpikeFrames}, {hit.Marker.Category.Name})",
                (float)(hit.ExcessMilliseconds / scale),
                ToolTheme.Error,
                hit.Marker.Name));
        }
    }

    private void BuildHotRows(List<Row> rows)
    {
        rows.Clear();
        if (_sweep.Running)
        {
            rows.Add(new Row(
                RowKind.Header,
                $"ПРОЧЁСЫВАНИЕ: {_sweep.Processed} ИЗ {_sweep.Total} МАРКЕРОВ"));
            rows.Add(new Row(RowKind.Text, "Каждая пачка копит несколько кадров; результаты появятся после прохода."));
            return;
        }

        if (!_sweep.HasResults)
        {
            rows.Add(new Row(
                RowKind.Text,
                "Кнопка «Прочесать все маркеры» по очереди записывает все временные маркеры " +
                "Unity и оставляет самые горячие: пассы RenderGraph, UI, всё неразмеченное. " +
                "Проход занимает несколько секунд."));
            return;
        }

        rows.Add(new Row(RowKind.Header, $"САМЫЕ ГОРЯЧИЕ {_sweep.Hits.Count} ИЗ {_sweep.Total}"));
        rows.Add(new Row(
            RowKind.Text,
            "Пачки измерены в разные кадры; значения суммируют потоки и вложенные вызовы. " +
            "Это список кандидатов, не раскладка одного кадра. UI включает окна редактора."));
        double scale = BudgetMilliseconds;
        foreach (HotMarkerSweep.Hit hit in _sweep.Hits)
        {
            scale = Math.Max(scale, hit.AverageMilliseconds);
        }

        foreach (HotMarkerSweep.Hit hit in _sweep.Hits)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{hit.Marker.Name}   {hit.AverageMilliseconds:F2} мс  (пик {hit.PeakMilliseconds:F2}, {hit.Marker.Category.Name})",
                (float)(hit.AverageMilliseconds / scale),
                ToolTheme.Error,
                hit.Marker.Name));
        }
    }

    private void BuildStageRows(List<Row> rows)
    {
        rows.Clear();
        rows.Add(new Row(RowKind.Header, "ПРОЦЕССОР — УЧАСТКИ"));
        AddStageGroup(rows, _cpu, ToolTheme.Warning);

        rows.Add(new Row(RowKind.Header, "ПЕРЕСБОРКИ — ПРИЧИНЫ"));
        var ledger = Fodinae.Core.Interfaces.Diagnostics.RebuildLedger.Entries;
        if (ledger.Count == 0)
        {
            rows.Add(new Row(RowKind.Text, "Пересборок в этом сеансе не было."));
        }

        foreach (var entry in ledger)
        {
            int rate = Fodinae.Core.Interfaces.Diagnostics.RebuildLedger.RateOf(entry);
            string ago = entry.LastTime < 0f
                ? "не было"
                : $"{Time.unscaledTime - entry.LastTime:F1} с назад";
            rows.Add(new Row(
                rate > 0 ? RowKind.Warning : RowKind.Text,
                $"{entry.Name}   за секунду {rate}, всего {entry.Total:N0}, последняя {ago}"));
        }

        rows.Add(new Row(RowKind.Header, "ВИДЕОКАРТА — ВРЕМЯ ИСПОЛНЕНИЯ"));
        rows.Add(new Row(
            SystemInfo.supportsGpuRecorder ? RowKind.Text : RowKind.Warning,
            SystemInfo.supportsGpuRecorder
                ? $"GPU-рекордеры поддерживаются ({SystemInfo.graphicsDeviceType}). Нули значат, что пасс не оборачивает команды в сэмплер."
                : $"GPU-рекордеры не поддерживаются на {SystemInfo.graphicsDeviceType} в этом режиме: время по пассам недоступно, нули ниже — не замер."));
        if (_timings.GpuSampledFrames > 0)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"Последние доступные GPU-замеры (FrameTimingManager): {_timings.Gpu.Average:F2} мс, пик {_timings.Gpu.Peak:F2}"));
        }

        AddStageGroup(rows, _gpu, ToolTheme.FrameGraphColor);

        rows.Add(new Row(RowKind.Header, "ВИДЕОКАРТА — ЗАПИСЬ КОМАНД НА ПРОЦЕССОРЕ"));
        AddProbeList(rows, _gpuRecord, ToolTheme.FrameGraphColor, pinnable: true);

        rows.Add(new Row(RowKind.Header, $"НАШИ МАРКЕРЫ ВНЕ ПЕРЕЧНЯ ({_discovered.Count})"));
        if (_discovered.Count == 0)
        {
            rows.Add(new Row(RowKind.Text, "Все сработавшие маркеры Fodinae.* уже в перечне."));
        }

        AddProbeList(rows, _discovered, ToolTheme.Accent, pinnable: true);
    }

    private void BuildToolRows(List<Row> rows)
    {
        rows.Clear();
        _toolWindows.Clear();
        double total = 0d;
        int events = 0;
        foreach (ToolWindow window in ToolWindows.All)
        {
            if (!window.Visible)
            {
                continue;
            }

            _toolWindows.Add(window);
            total += window.DrawMilliseconds;
            events += window.DrawEvents;
        }

        _toolWindows.Sort(static (left, right) => right.DrawMilliseconds.CompareTo(left.DrawMilliseconds));
        rows.Add(new Row(RowKind.Header, $"ОКНА ИНСТРУМЕНТОВ — {total:F2} мс ЗА КАДР"));
        rows.Add(new Row(
            RowKind.Text,
            $"Секундомер внутри каждого окна, {events} событий IMGUI за кадр. Окна редактора сюда не попадают."));
        double scale = Math.Max(total, 1d);
        foreach (ToolWindow window in _toolWindows)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{window.Title}   {window.DrawMilliseconds:F2} мс   ({window.DrawEvents} соб.)   " +
                $"мусор: Tick {window.TickAllocatedBytes / 1024d:F1} КБ, отрисовка {window.DrawAllocatedBytes / 1024d:F1} КБ",
                (float)(window.DrawMilliseconds / scale),
                ToolTheme.Accent));
        }

        rows.Add(new Row(
            RowKind.Header,
            Application.isEditor ? "МАРКЕРЫ ИНТЕРФЕЙСА — ВМЕСТЕ С ОКНАМИ РЕДАКТОРА" : "МАРКЕРЫ ИНТЕРФЕЙСА"));
        AddProbeList(rows, _interface, ToolTheme.Warning, pinnable: true);
    }

    private void BuildMemoryRows(List<Row> rows)
    {
        rows.Clear();
        rows.Add(new Row(RowKind.Header, "ПАМЯТЬ"));
        foreach (FrameProbe probe in _memory)
        {
            AddCounter(rows, probe);
        }

        rows.Add(new Row(RowKind.Header, "СЧЁТЧИКИ РЕНДЕРА"));
        foreach (FrameProbe probe in _render)
        {
            AddCounter(rows, probe);
        }
    }

    private void BuildSceneRows(List<Row> rows)
    {
        rows.Clear();
        SceneCensus.Snapshot? census = _census.Last;
        if (census == null)
        {
            rows.Add(new Row(RowKind.Text, "Перепись идёт, пока открыта эта вкладка; первый снимок — через кадр."));
            return;
        }

        rows.Add(new Row(RowKind.Header, "ЖИВАЯ ИЕРАРХИЯ"));
        rows.Add(new Row(
            RowKind.Text,
            $"Объектов {census.Objects:N0}, активных {census.ActiveObjects:N0}; компонентов {census.Components:N0}"));
        rows.Add(new Row(
            RowKind.Text,
            $"Рендереров {census.Renderers:N0}, включённых {census.RenderersEnabled:N0}, видимых камерой {census.RenderersVisible:N0}"));
        rows.Add(new Row(RowKind.Text, $"Глубина иерархии до {census.MaxDepth}: {census.DeepestPath}"));
        if (census.MissingScripts > 0)
        {
            rows.Add(new Row(RowKind.Warning, $"Компонентов с потерянным скриптом: {census.MissingScripts}"));
        }

        rows.Add(new Row(
            RowKind.Text,
            $"Обход занял {census.WalkMilliseconds:F2} мс, раз в {SceneCensus.IntervalSeconds:F0} с — это цена замера, не кадра игры."));

        rows.Add(new Row(RowKind.Header, "СЦЕНЫ"));
        foreach (SceneCensus.SceneStat scene in census.Scenes)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{scene.Name}   корней {scene.Roots}, объектов {scene.Objects:N0}, активных {scene.ActiveObjects:N0}",
                census.Objects > 0 ? scene.Objects / (float)census.Objects : -1f));
        }

        rows.Add(new Row(RowKind.Header, "САМЫЕ КРУПНЫЕ КОРНИ"));
        foreach (SceneCensus.RootStat root in census.Roots)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{root.Scene} / {root.Name}{(root.Active ? string.Empty : "  (выключен)")}   объектов {root.Objects:N0}, активных {root.ActiveObjects:N0}",
                census.Objects > 0 ? root.Objects / (float)census.Objects : -1f));
        }

        rows.Add(new Row(RowKind.Header, "КАМЕРЫ"));
        if (census.Cameras.Count == 0)
        {
            rows.Add(new Row(RowKind.Warning, "Камер не найдено."));
        }

        foreach (string camera in census.Cameras)
        {
            rows.Add(new Row(RowKind.Text, camera));
        }

        rows.Add(new Row(RowKind.Header, "КОМПОНЕНТЫ ПО ТИПАМ"));
        foreach ((string type, int total, int enabled) in census.ComponentTypes)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{type}   {total:N0}, включённых {enabled:N0}",
                census.Components > 0 ? total / (float)census.Components : -1f));
        }
    }

    private void BuildSearchRows(List<Row> rows)
    {
        rows.Clear();
        if (_search.QueryTooShort)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"Введите хотя бы 2 символа. Всего маркеров и счётчиков: {MarkerDirectory.All.Count}."));
            return;
        }

        rows.Add(new Row(
            RowKind.Header,
            _search.MatchCount > MarkerSearch.MaxResults
                ? $"НАЙДЕНО {_search.MatchCount}, ЗАПИСЫВАЮТСЯ ПЕРВЫЕ {MarkerSearch.MaxResults}"
                : $"НАЙДЕНО {_search.MatchCount}"));
        AddProbeList(rows, _search.Results, ToolTheme.Accent, pinnable: true);
    }

    private static void AddSeries(List<Row> rows, string title, RollingSeries series, double scale, Color color)
    {
        rows.Add(new Row(
            RowKind.Text,
            $"{title}   {series.Average:F2} мс  (пик {series.Peak:F2}, последний {series.Last:F2})",
            (float)(series.Average / scale),
            color));
    }

    private void AddProbeList(
        List<Row> rows,
        IReadOnlyList<FrameProbe> probes,
        Color color,
        bool pinnable,
        double scale = 0d)
    {
        if (scale <= 0d)
        {
            scale = BudgetMilliseconds;
            foreach (FrameProbe probe in probes)
            {
                if (probe.Available && probe.IsTime)
                {
                    scale = Math.Max(scale, probe.Peak);
                }
            }
        }

        foreach (FrameProbe probe in probes)
        {
            AddProbe(rows, probe, scale, color, pinnable: pinnable);
        }
    }

    private void AddProbe(
        List<Row> rows,
        FrameProbe probe,
        double scale,
        Color color,
        string? title = null,
        bool pinnable = false)
    {
        string label = title ?? probe.Title;
        string? pin = pinnable ? probe.MarkerName : null;
        if (probe.GpuUnsupported)
        {
            rows.Add(new Row(RowKind.Text, $"{label}  —  у маркера нет GPU-метки", Pin: pin));
            return;
        }

        if (!probe.Available)
        {
            rows.Add(new Row(RowKind.Text, $"{label}  —  маркер не сработал в этом сеансе", Pin: pin));
            return;
        }

        if (!probe.IsTime)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{label}   {probe.FormatValue(probe.Last)}  (пик {probe.FormatValue(probe.Peak)})",
                Pin: pin));
            return;
        }

        rows.Add(new Row(
            RowKind.Text,
            $"{label}   {probe.Average:F2} мс  (пик {probe.Peak:F2}, последний {probe.Last:F2})",
            (float)(probe.Average / scale),
            color,
            pin));
    }

    private static void AddCounter(List<Row> rows, FrameProbe probe)
    {
        rows.Add(new Row(
            RowKind.Text,
            probe.Available
                ? $"{probe.Title}: {probe.FormatValue(probe.Last)}   (пик {probe.FormatValue(probe.Peak)})"
                : $"{probe.Title}: счётчика нет в этой сборке"));
    }

    private void AddStageGroup(List<Row> rows, List<FrameProbe> probes, Color color)
    {
        SortByStage(probes);
        double scale = BudgetMilliseconds;
        foreach (FrameProbe probe in probes)
        {
            if (probe.Available)
            {
                scale = Math.Max(scale, probe.Peak);
            }
        }

        foreach (FrameProbe probe in probes)
        {
            AddProbe(rows, probe, scale, color, pinnable: true);
        }
    }

    private void SortByStage(List<FrameProbe> probes)
    {
        _stageOrder.Clear();
        for (int i = 0; i < probes.Count; i++)
        {
            if (probes[i].IsDetail)
            {
                continue;
            }

            int end = i + 1;
            double weight = probes[i].Available ? probes[i].Average : 0d;
            while (end < probes.Count && probes[end].IsDetail)
            {
                if (!probes[i].Available && probes[end].Available)
                {
                    weight = Math.Max(weight, probes[end].Average);
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

        // Строка-деталь без этапа над собой возможна только при ошибке в
        // перечне; терять её молча нельзя, поэтому она дописывается в конец.
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

    private readonly List<Fodinae.Core.Interfaces.Diagnostics.AllocationLedger.Entry> _ledgerSorted = [];

    // У ledger и счётчика GC разные окна выборок. Их разность не является
    // измерением аллокаций редактора или UI.
    private void AddAllocationLedgerRows(List<Row> rows)
    {
        rows.Add(new Row(RowKind.Header, "МУСОР ПО ИГРОВЫМ ПУТЯМ (СРЕДНЕЕ ЗА КАДР)"));
        _ledgerSorted.Clear();
        _ledgerSorted.AddRange(Fodinae.Core.Interfaces.Diagnostics.AllocationLedger.Entries);
        _ledgerSorted.Sort(static (left, right) => right.AverageBytes.CompareTo(left.AverageBytes));
        if (_ledgerSorted.Count == 0)
        {
            rows.Add(new Row(RowKind.Text, "Ни один замеренный путь ещё не выполнялся."));
            return;
        }

        double scale = 1024d;
        foreach (var entry in _ledgerSorted)
        {
            scale = Math.Max(scale, entry.AverageBytes);
        }

        foreach (var entry in _ledgerSorted)
        {
            rows.Add(new Row(
                RowKind.Text,
                $"{entry.Name}   {entry.AverageBytes / 1024d:F2} КБ  (пик {entry.PeakBytes / 1024d:F1}, " +
                $"последний {entry.LastFrameBytes / 1024d:F2}, вызовов {entry.LastFrameCalls}" +
                (entry.CollectionsInside > 0 ? $", сборок внутри {entry.CollectionsInside}" : string.Empty) + ")",
                (float)(entry.AverageBytes / scale),
                ToolTheme.Warning));
        }

        rows.Add(new Row(RowKind.Text,
            "Пути могут быть вложены. Общий GC, окна и ledger измерены отдельно; остаток не вычисляется."));
    }

    private static long ToolAllocatedBytes()
    {
        long total = 0;
        foreach (ToolWindow window in ToolWindows.All)
        {
            total += window.TickAllocatedBytes + window.DrawAllocatedBytes;
        }

        return total;
    }

    private string BuildReport()
    {
        SampleAll();
        RebuildAllRows();
        var report = new StringBuilder(8192);
        report.Append("Разбор кадра, ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(Application.isEditor ? ", редактор" : ", сборка")
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height)
            .Append(", ").Append(SystemInfo.graphicsDeviceType)
            .AppendLine();

        foreach ((Tab tab, string label) in _Tabs)
        {
            if (tab == Tab.Search && _search.QueryTooShort)
            {
                continue;
            }

            report.AppendLine().Append("=== ").Append(label.ToUpperInvariant()).AppendLine(" ===");
            foreach (Row row in _rows[tab])
            {
                if (row.Kind == RowKind.Header)
                {
                    report.AppendLine().Append("## ").AppendLine(row.Text);
                }
                else
                {
                    report.AppendLine(row.Text);
                }
            }
        }

        return report.ToString();
    }

    protected override void DrawContent()
    {
        if (Event.current.type == EventType.Layout && _tab != _requestedTab)
        {
            _tab = _requestedTab;

            // Строки новой вкладки ещё не собраны: пересборка на ближайшем Tick.
            _nextUpdate = 0f;
        }

        using (ToolLayout.Horizontal())
        {
            foreach ((Tab tab, string label) in _Tabs)
            {
                if (GUILayout.Toggle(_tab == tab, label, SegmentedButtonStyle) && _tab != tab)
                {
                    _requestedTab = tab;
                }
            }
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button(
                    _copyLabel,
                    SecondaryButtonStyle))
            {
                _copyRequested = true;
            }

            GUILayout.Label("обновление 250 мс", MutedLabelStyle);
        }

        if (_tab == Tab.Search)
        {
            _search.Query = GUILayout.TextField(_search.Query);
        }

        if (_tab == Tab.Layout)
        {
            using (ToolLayout.Horizontal())
            {
                if (GUILayout.Button("Сохранить TODO раскладки", ActiveButtonStyle))
                {
                    _layoutTodoRequested = true;
                }

                if (GUILayout.Button("Сбросить счёт", SecondaryButtonStyle))
                {
                    _layoutResetRequested = true;
                }
            }
        }

        if (_tab == Tab.Hot &&
            GUILayout.Button(
                _sweepLabel,
                ActiveButtonStyle) &&
            !_sweep.Running)
        {
            _sweepRequested = true;
        }

        if (_tab == Tab.Spikes &&
            GUILayout.Button(
                _spikes.Running ? "Идёт проход…" : "Поймать всплески",
                ActiveButtonStyle) &&
            !_spikes.Running)
        {
            _spikesRequested = true;
        }

        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (!_profilerAvailable && _tab != Tab.Tools)
            {
                ToolChrome.Banner("МАРКЕРЫ НЕДОСТУПНЫ", ToolTheme.Warning);
                GUILayout.Label(
                    "В этой сборке не определён ENABLE_PROFILER. Он есть в вариантах " +
                    "Instrumented, Checked и Debug, но не в Release. Вкладка «Окна» " +
                    "и тайминги потоков работают и без него.",
                    WrappedLabelStyle);
            }

            DrawRows(_rows[_tab]);
        }

        if (Event.current.type == EventType.Repaint)
        {
            _viewportHeight = GUILayoutUtility.GetLastRect().height;
        }
    }

    // Отрисовка только видимых строк.
    //
    // Раньше каждое событие IMGUI проводило через GUILayout все строки вкладки —
    // на «Цикле» и «Горячем» это 60–180 подписей с полосками, и окно само стоило
    // до 8.7 мс кадра. Теперь строка вне видимой части заменяется пустым местом
    // той же высоты. Высота строки не постоянная (подписи переносятся), поэтому
    // она не вычисляется, а замеряется при отрисовке и запоминается; строка с
    // неизвестной высотой рисуется целиком. Замеры применяются после прохода,
    // чтобы Layout и Repaint одного кадра видели один и тот же набор элементов.
    private const float RowCullMargin = 120f;
    private float[] _rowHeights = [];
    private float[] _measuredHeights = [];
    private Tab _rowHeightsTab;
    private float _viewportHeight = float.MaxValue;

    private void DrawRows(List<Row> rows)
    {
        if (rows.Count == 0)
        {
            GUILayout.Label("Замеров ещё нет.", MutedLabelStyle);
            return;
        }

        if (_rowHeightsTab != _tab || _rowHeights.Length != rows.Count)
        {
            if (_rowHeightsTab != _tab)
            {
                _rowHeights = [];
            }

            Array.Resize(ref _rowHeights, rows.Count);
            _measuredHeights = new float[rows.Count];
            _rowHeightsTab = _tab;
        }

        bool repaint = Event.current.type == EventType.Repaint;
        float top = _scroll.y - RowCullMargin;
        float bottom = _scroll.y + _viewportHeight + RowCullMargin;
        float y = 0f;

        for (int i = 0; i < rows.Count; i++)
        {
            float known = _rowHeights[i];
            if (known > 0f && (y + known < top || y > bottom))
            {
                GUILayout.Space(known);
                _measuredHeights[i] = known;
                y += known;
                continue;
            }

            Rect start = GUILayoutUtility.GetRect(0f, 0f);
            DrawRow(rows[i]);
            Rect end = GUILayoutUtility.GetRect(0f, 0f);
            _measuredHeights[i] = repaint ? end.y - start.y : known;
            y += known > 0f ? known : 0f;
        }

        if (repaint)
        {
            Array.Copy(_measuredHeights, _rowHeights, rows.Count);
        }
    }

    private void DrawRow(Row row)
    {
        switch (row.Kind)
        {
            case RowKind.Header:
                ToolChrome.SectionHeader(row.Text);
                return;
            case RowKind.Warning:
                GUILayout.Label(row.Text, ToolTheme.WarningLabel);
                return;
        }

        if (row.Pin == null)
        {
            GUILayout.Label(row.Text, MutedLabelStyle);
        }
        else
        {
            using (ToolLayout.Horizontal())
            {
                GUILayout.Label(row.Text, MutedLabelStyle);
                bool pinned = _search.IsPinned(row.Pin);
                if (GUILayout.Button(pinned ? "★" : "☆", SecondaryButtonStyle, ToolLayout.Width(26f)))
                {
                    _search.RequestTogglePin(row.Pin);
                }
            }
        }

        if (row.Share >= 0f)
        {
            ToolChrome.MeterLine(row.Share, row.Color, 3f);
            GUILayout.Space(2f);
        }
    }
}
