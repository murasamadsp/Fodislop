#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

/// <summary>
/// Во что обходится одно решение света, по каскадам.
/// </summary>
/// <remarks>
/// Числа считались и раньше — <see cref="CascadeCostCalculator"/> написан,
/// покрыт тестами и повторяет арифметику самого шейдера, — но не показывались
/// нигде. Именно их не хватало, когда кадр проседал при ходьбе: цена решения
/// выяснялась вычислениями на бумаге, хотя движок знал её сам.
///
/// Показывается стоимость ПОЛНОГО решения. Динамическая половина решается по
/// трём каскадам вместо всех, поэтому её строка дешевле; в шапке написано,
/// сколько решений какой половины прошло за последнюю секунду, — по этим двум
/// числам и видно, за что платит кадр.
/// </remarks>
public sealed class LightingCostWindow : ToolWindow
{
    /// <summary>Обновление раз в полсекунды: раскладка каскадов так часто не меняется.</summary>
    private const float RefreshInterval = 0.5f;

    private readonly LightingEngine? _lighting;
    private readonly IFrameTelemetry _telemetry;
    private readonly List<CascadeCostSample> _samples = [];
    private readonly List<string> _rows = [];

    private long _totalRaySteps;
    private long _totalMergeTaps;
    private long _heaviestRaySteps;
    private string _summary = "нет данных";
    private string _solveMix = "решений: --";
    private float _nextUpdate;
    private Vector2 _scroll;

    public LightingCostWindow(LightingEngine? lighting, IFrameTelemetry telemetry)
        : base("Цена света", new Rect(292f, 382f, 320f, 340f))
    {
        _lighting = lighting;
        _telemetry = telemetry;
    }

    /// <summary>Данных не копит: всё берётся у движка в момент опроса.</summary>
    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(280f, 260f);

    public override void Tick()
    {
        if (!Visible || _lighting == null || Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        if (!_lighting.IsInitialized)
        {
            _summary = "движок света ещё не готов";
            _rows.Clear();
            return;
        }

        _lighting.CollectCascadeCosts(_samples);
        Recalculate();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        _samples.Clear();
        _rows.Clear();
        _totalRaySteps = 0;
        _totalMergeTaps = 0;
        _heaviestRaySteps = 0;
        _summary = "нет данных";
        _solveMix = "решений: --";
    }

    private void Recalculate()
    {
        _rows.Clear();
        _totalRaySteps = 0;
        _totalMergeTaps = 0;
        _heaviestRaySteps = 0;

        foreach (CascadeCostSample sample in _samples)
        {
            _totalRaySteps += sample.RayStepCount;
            _totalMergeTaps += sample.MergeTapCount;
            _heaviestRaySteps = System.Math.Max(_heaviestRaySteps, sample.RayStepCount);
            _rows.Add(
                $"К{sample.Index}  {sample.DirectionCount} напр  {sample.ProbeWidth}×{sample.ProbeHeight}  " +
                $"шагов {sample.StepCount}  ·  {Millions(sample.RayStepCount)} шагов луча");
        }

        _summary =
            $"{Millions(_totalRaySteps)} шагов луча  +  {Millions(_totalMergeTaps)} выборок атласа " +
            $"за одно полное решение";
        _solveMix =
            $"за секунду: статических {_telemetry.LightingStaticSolveCount}, " +
            $"динамических {_telemetry.LightingDynamicSolveCount}";
    }

    /// <summary>
    /// Миллионы, а не полное число.
    /// </summary>
    /// <remarks>
    /// «46 900 000» и «69 800 000» глазом не различаются и не запоминаются.
    /// Разница между «46.9 М» и «703.1 М» видна сразу — а именно её и надо
    /// увидеть, потому что она и есть двадцатикратная просадка.
    /// </remarks>
    private static string Millions(long value) =>
        value >= 1_000_000
            ? $"{value / 1_000_000.0:F1} М"
            : $"{value / 1000.0:F0} К";

    protected override void DrawContent()
    {
        if (_lighting == null)
        {
            GUILayout.Label("Движок освещения недоступен.", MutedLabelStyle);
            return;
        }

        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;

            if (_lighting.BypassLightingCompute)
            {
                ToolChrome.Banner("РАСЧЁТ ОБОЙДЁН", ToolTheme.Error);
                GUILayout.Space(4f);
            }

            ToolChrome.SectionHeader("ПОЛНОЕ РЕШЕНИЕ");
            GUILayout.Label(_summary, MutedLabelStyle);
            GUILayout.Label(_solveMix, MutedLabelStyle);

            ToolChrome.SectionHeader("КАСКАДЫ");
            DrawCascadeRows();
            DrawLimits();
        }
    }

    /// <summary>
    /// Строка на каскад: текст и полоса доли.
    /// </summary>
    /// <remarks>
    /// Доля считается от самого дорогого каскада, а не от суммы. Смысл вопроса
    /// не «сколько процентов набрал этот», а «какой из них здесь главный»: у
    /// нижнего каскада шагов на порядок больше, и на шкале от суммы все
    /// остальные схлопнулись бы в невидимые огрызки.
    /// </remarks>
    private void DrawCascadeRows()
    {
        if (_rows.Count == 0)
        {
            GUILayout.Label("Раскладка каскадов ещё не собрана.", MutedLabelStyle);
            return;
        }

        for (int i = 0; i < _rows.Count && i < _samples.Count; i++)
        {
            GUILayout.Label(_rows[i], MutedLabelStyle);
            float share = _heaviestRaySteps > 0
                ? _samples[i].RayStepCount / (float)_heaviestRaySteps
                : 0f;
            ToolChrome.MeterLine(share, i == 0 ? ToolTheme.Warning : ToolTheme.FrameGraphColor);
            GUILayout.Space(3f);
        }
    }

    /// <summary>Упёрлось ли качество в потолок — и в какой именно.</summary>
    private void DrawLimits()
    {
        if (_lighting == null)
        {
            return;
        }

        ToolChrome.SectionHeader("ПРЕДЕЛЫ");
        DrawLimitRow(
            "Разрешение поля",
            _lighting.TextureDimensionLimited,
            $"{_lighting.FieldWidth}×{_lighting.FieldHeight} при {_lighting.EffectivePixelsPerCell:F2} пикс/клетку");
        DrawLimitRow(
            "Число каскадов",
            _lighting.CascadeBudgetLimited,
            $"{_lighting.CascadeCount} каскадов, шагов до {_lighting.MaximumIntervalSteps}");
        DrawLimitRow(
            "Атлас проб",
            false,
            $"{_lighting.AtlasEntryCount} записей, источников {_lighting.DynamicLightCount}");
    }

    private static void DrawLimitRow(string title, bool limited, string detail)
    {
        using (new GUILayout.HorizontalScope())
        {
            ToolChrome.StatusPip(limited ? ToolTheme.Warning : ToolTheme.Success);
            using (new GUILayout.VerticalScope())
            {
                GUILayout.Label(limited ? $"{title} — упёрлось в потолок" : title, WrappedLabelStyle);
                GUILayout.Label(detail, MutedLabelStyle);
            }
        }

        GUILayout.Space(3f);
    }
}
