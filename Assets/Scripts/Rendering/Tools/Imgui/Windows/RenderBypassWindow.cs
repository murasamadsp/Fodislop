#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting;
using Kern.World.Terrain;
using Kern.Game;
using Kern.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tools.Imgui.Windows;

public sealed class RenderBypassWindow : ToolWindow
{
    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly IClientConfigManager? _clientConfig;
    private readonly TerrainRenderer? _terrainRenderer;
    private readonly LightingEngine? _lighting;
    private readonly WorldGizmoOptions _gizmos;
    private readonly SurfaceRenderer? _surfaceRenderer;
    private readonly WorldEntityBatchRenderer? _entityRenderer;
    private readonly UIDocument? _gameUIDocument;
    private bool _hideSurface;
    private bool _hideEntities;
    private bool _hideGameUI;
    private Vector2 _scroll;
    private int _bypassBannerCount = -1;
    private string _bypassBanner = string.Empty;
    private LightingEngine.DebugView? _lightingViewLabelValue;
    private string _lightingViewLabel = string.Empty;

    public RenderBypassWindow(
        IRuntimeDebugSettings debugSettings,
        LightingEngine? lighting,
        WorldGizmoOptions gizmos,
        SurfaceRenderer? surfaceRenderer = null,
        WorldEntityBatchRenderer? entityRenderer = null,
        UIDocument? gameUIDocument = null,
        IClientConfigManager? clientConfig = null,
        TerrainRenderer? terrainRenderer = null)
        : base("Диагностика рендера", new Rect(16f, 382f, 260f, 390f))
    {
        _debugSettings = debugSettings;
        _clientConfig = clientConfig;
        _terrainRenderer = terrainRenderer;
        _lighting = lighting;
        _gizmos = gizmos;
        _surfaceRenderer = surfaceRenderer;
        _entityRenderer = entityRenderer;
        _gameUIDocument = gameUIDocument;
        _sweepSteps =
        [
            ("Расчёт освещения", on => _debugSettings.BypassLightingCompute = on),

            // Внутри света отдельно: фонари роботов перетрассируются при
            // каждом сдвиге, статика — только при смене геометрии.
            ("Динамический свет", on =>
            {
                if (LightingConfigHolder.DynamicLightEnabled == on)
                {
                    ToggleDynamicLight();
                }
            }),
            ("Отрисовка террейна", on => _debugSettings.BypassTerrainDraw = on),
            ("Проходы постпроцесса", on => PostProcessRuntimeState.SkipPasses = on),
            ("Эффекты постпроцесса", on => PostProcessRuntimeState.BypassPostProcessEffects = on),
            ("Поверхность", on => { _hideSurface = on; ApplyVisibility(); }),
            ("Сущности мира", on => { _hideEntities = on; ApplyVisibility(); }),
            ("Интерфейс игры", on => { _hideGameUI = on; ApplyVisibility(); }),

            // Террейн без всего остального, а затем он же с ровной решёткой:
            // разница — цена смещённой геометрии (носители шире клетки,
            // органическое покрытие, фон под смещённой породой).
            ("Только террейн", SetEverythingButTerrainBypassed),
            ("Только террейн, без искажения сетки", on =>
            {
                SetEverythingButTerrainBypassed(on);
                SetDistortion(on ? false : _sweepDistortion);
            }),
        ];
    }

    public override void Tick()
    {
        if (_sweepStep < 0)
        {
            return;
        }

        _sweepFrame++;
        if (_sweepFrame <= SweepSettleFrames)
        {
            return;
        }

        _sweepFrameMs.Add(Time.unscaledDeltaTime * 1000.0);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _sweepTimings) > 0 && _sweepTimings[0].gpuFrameTime > 0.0)
        {
            _sweepGpuMs.Add(_sweepTimings[0].gpuFrameTime);
        }

        if (_sweepFrame < SweepSettleFrames + SweepMeasureFrames)
        {
            return;
        }

        _sweepResults.Add((SweepLabel(_sweepStep), Median(_sweepFrameMs), Median(_sweepGpuMs), _sweepGpuMs.Count));
        SetSweepStep(_sweepStep, false);
        _sweepStep++;
        if (_sweepStep > _sweepSteps.Count + 1)
        {
            FinishSweep();
            return;
        }

        SetSweepStep(_sweepStep, true);
        _sweepFrame = 0;
        _sweepFrameMs.Clear();
        _sweepGpuMs.Clear();
    }

    // Шаг 0 и последний — кадр без обходов: разница между ними показывает,
    // насколько за время замера уплыл сам кадр.
    private string SweepLabel(int step) =>
        step >= 1 && step <= _sweepSteps.Count ? _sweepSteps[step - 1].Label :
        step == 0 ? "без обходов" : "без обходов (повтор)";

    private void SetSweepStep(int step, bool on)
    {
        if (step >= 1 && step <= _sweepSteps.Count)
        {
            _sweepSteps[step - 1].Apply(on);
        }
    }

    private void SetEverythingButTerrainBypassed(bool on)
    {
        _debugSettings.BypassLightingCompute = on;
        PostProcessRuntimeState.SkipPasses = on;
        PostProcessRuntimeState.BypassPostProcessEffects = on;
        _hideSurface = on;
        _hideEntities = on;
        _hideGameUI = on;
        ApplyVisibility();
    }

    private void SetDistortion(bool enabled)
    {
        if (_clientConfig?.Config == null || _terrainRenderer == null ||
            _clientConfig.Config.Terrain.EnableDistortion == enabled)
        {
            return;
        }

        _clientConfig.UpdateSection(config => config.Terrain, terrain => terrain.EnableDistortion = enabled);
        _terrainRenderer.ApplyClientConfig();
    }

    private void StartSweep()
    {
        _sweepDistortion = _clientConfig?.Config?.Terrain.EnableDistortion ?? true;
        foreach ((_, Action<bool> apply) in _sweepSteps)
        {
            apply(false);
        }

        _sweepResults.Clear();
        _sweepFrameMs.Clear();
        _sweepGpuMs.Clear();
        _sweepFrame = 0;
        _sweepStep = 0;
        _sweepSummary = string.Empty;
    }

    private void FinishSweep()
    {
        _sweepStep = -1;
        double baseFrame = (_sweepResults[0].FrameMs + _sweepResults[^1].FrameMs) * 0.5;
        double baseGpu = (_sweepResults[0].GpuMs + _sweepResults[^1].GpuMs) * 0.5;
        var text = new StringBuilder(1024);
        text.Append("Кадр без обходов: ").Append(baseFrame.ToString("F2")).Append(" мс, GPU ")
            .Append(baseGpu.ToString("F2")).Append(" мс (первый ").Append(_sweepResults[0].FrameMs.ToString("F2"))
            .Append(", повтор ").Append(_sweepResults[^1].FrameMs.ToString("F2")).AppendLine(")");
        text.AppendLine("Что снимает обход (медиана кадра; GPU — по доступным замерам FrameTimingManager):");
        for (int index = 1; index < _sweepResults.Count - 1; index++)
        {
            (string label, double frameMs, double gpuMs, int gpuSamples) = _sweepResults[index];
            text.Append("  ").Append(label).Append(": кадр ").Append(frameMs.ToString("F2"))
                .Append(" мс (").Append((baseFrame - frameMs).ToString("+0.00;-0.00")).Append("), GPU ")
                .Append(gpuMs.ToString("F2")).Append(" мс (").Append((baseGpu - gpuMs).ToString("+0.00;-0.00"))
                .Append(", замеров ").Append(gpuSamples).AppendLine(")");
        }

        _sweepSummary = text.ToString();
        DiagnosticReport.Write("Performance", "bypass_cost", "Цена этапов кадра", _sweepSummary);
        Debug.Log("[BypassCost]\n" + _sweepSummary);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    // Замер цены этапов: каждый обход по очереди на полторы-две секунды,
    // медиана кадра и GPU-время против той же медианы без обходов. Время
    // проходов на Metal в редакторе недоступно, а разница с обходом и без
    // него меряет настоящий путь рендера — те же шейдеры, те же проходы.
    private const int SweepSettleFrames = 45;
    private const int SweepMeasureFrames = 150;

    private readonly List<(string Label, Action<bool> Apply)> _sweepSteps;
    private readonly List<double> _sweepFrameMs = new(SweepMeasureFrames);
    private readonly List<double> _sweepGpuMs = new(SweepMeasureFrames);
    private readonly List<(string Label, double FrameMs, double GpuMs, int GpuSamples)> _sweepResults = [];
    private readonly FrameTiming[] _sweepTimings = new FrameTiming[1];
    private int _sweepStep = -1;
    private bool _sweepDistortion;
    private int _sweepFrame;
    private string _sweepSummary = string.Empty;

    public override bool WantsSampling => _sweepStep >= 0;

    public override Vector2 MinimumSize => new(250f, 330f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _sweepStep = -1;
        _debugSettings.BypassLightingCompute = false;
        _debugSettings.BypassTerrainDraw = false;
        _debugSettings.BypassCpuMeshRebuild = false;
        _debugSettings.ShowRobotDebugVisuals = false;
        PostProcessRuntimeState.SkipPasses = false;
        PostProcessRuntimeState.BypassPostProcessEffects = false;
        _hideSurface = false;
        _hideEntities = false;
        _hideGameUI = false;
        _lighting?.SetDebugView(LightingEngine.DebugView.FinalLighting);
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            DrawBypassWarning();
            DrawSweep();

            ToolChrome.SectionHeader("ОБХОДЫ");
            GUILayout.Label("Активный пункт отключает соответствующий этап.", MutedLabelStyle);
            _debugSettings.BypassLightingCompute = DrawSwitch(
                _debugSettings.BypassLightingCompute, "Расчёт освещения");
            _debugSettings.BypassTerrainDraw = DrawSwitch(
                _debugSettings.BypassTerrainDraw, "Отрисовка террейна");
            _debugSettings.BypassCpuMeshRebuild = DrawSwitch(
                _debugSettings.BypassCpuMeshRebuild, "Пересборка меша");
            PostProcessRuntimeState.SkipPasses = DrawSwitch(
                PostProcessRuntimeState.SkipPasses, "Проходы постпроцесса");
            // Рядом, но это не одно и то же: выше проходы не ставятся в
            // очередь вовсе (цена самих проходов), ниже они идут, но без
            // эффектов (цена эффектов). Разделение и есть смысл пары.
            PostProcessRuntimeState.BypassPostProcessEffects = DrawSwitch(
                PostProcessRuntimeState.BypassPostProcessEffects, "Эффекты постпроцесса");
            _hideSurface = DrawSwitch(_hideSurface, "Поверхность");
            _hideEntities = DrawSwitch(_hideEntities, "Сущности мира");
            _hideGameUI = DrawSwitch(_hideGameUI, "Интерфейс игры");
            ApplyVisibility();
            _debugSettings.ShowRobotDebugVisuals = DrawSwitch(
                _debugSettings.ShowRobotDebugVisuals, "Отладка роботов", ToolTheme.FrameGraphColor);

            DrawTerrainGeometry();

            ToolChrome.SectionHeader("ГИЗМО В МИРЕ");
            _gizmos.ShowGrid = DrawSwitch(_gizmos.ShowGrid, "Сетка чанков", ToolTheme.FrameGraphColor);
            _gizmos.ShowCursor = DrawSwitch(_gizmos.ShowCursor, "Курсор клетки", ToolTheme.FrameGraphColor);

            if (_lighting == null)
            {
                return;
            }

            ToolChrome.SectionHeader("ДИНАМИЧЕСКИЙ СВЕТ");
            bool lit = LightingConfigHolder.DynamicLightEnabled;
            if (DrawSwitch(lit, "Динамический свет", ToolTheme.Success) != lit)
            {
                ToggleDynamicLight();
            }

            DrawLightingViewPicker(_lighting);
        }
    }

    // Искажение сетки террейна. Это настройка игрока (Terrain.EnableDistortion),
    // а не отдельный отладочный флаг: второй источник правды тут же разошёлся бы
    // с первым. Здесь она продублирована потому, что смотреть на неё надо на
    // живой сцене — меню паузы закрывает собой ровно ту картинку, по которой
    // сравниваешь ровную сетку с неровной.
    //
    // Смена флага заставляет TerrainRenderer.ApplyClientConfig пересобрать
    // меши, поэтому щелчок стоит кадра-другого и тут же виден.
    private void DrawTerrainGeometry()
    {
        if (_clientConfig?.Config == null || _terrainRenderer == null)
        {
            return;
        }

        ToolChrome.SectionHeader("ГЕОМЕТРИЯ ТЕРРЕЙНА");
        bool distortion = _clientConfig.Config.Terrain.EnableDistortion;
        if (DrawSwitch(distortion, "Искажение сетки", ToolTheme.Success) != distortion)
        {
            bool next = !distortion;
            _clientConfig.MarkGraphicsAsCustom();
            _clientConfig.UpdateSection(config => config.Terrain, terrain => terrain.EnableDistortion = next);
            _terrainRenderer.ApplyClientConfig();
        }

        GUILayout.Label(
            "Выключено — клетки стоят ровной решёткой. Включено — углы клеток " +
            "внутри массивов породы разъезжаются, и порода читается цельным " +
            "камнем, а не плиткой. Силуэт построек не трогается в обоих случаях.",
            MutedLabelStyle);

        bool rim = _clientConfig.Config.Terrain.EnableReliefRim;
        if (DrawSwitch(rim, "Кайма рельефа", ToolTheme.Success) != rim)
        {
            bool next = !rim;
            _clientConfig.MarkGraphicsAsCustom();
            _clientConfig.UpdateSection(config => config.Terrain, terrain => terrain.EnableReliefRim = next);
            _terrainRenderer.ApplyClientConfig();
        }

        GUILayout.Label(
            "Затемнение к границам, за которыми лежит чужая рельефная семья. " +
            "Выключение не убирает ни маску, ни транспорт — шейдер просто " +
            "перестаёт на неё умножать.",
            MutedLabelStyle);
    }

    // A/B для замера цены слоя: рендереры включаются и выключаются каждый
    // кадр отрисовки окна, поэтому пересозданные объекты слоя тоже скрываются.
    private void DrawSweep()
    {
        ToolChrome.SectionHeader("ЦЕНА ЭТАПОВ");
        if (_sweepStep >= 0)
        {
            GUILayout.Label(
                $"Замер: {SweepLabel(_sweepStep)} ({_sweepStep + 1} из {_sweepSteps.Count + 2}). Не трогайте игру.",
                WrappedLabelStyle);
        }
        else if (GUILayout.Button("Замерить цену этапов (~1 мин)", ActiveButtonStyle))
        {
            StartSweep();
        }

        if (_sweepSummary.Length > 0)
        {
            GUILayout.Label(_sweepSummary, WrappedLabelStyle);
        }
    }

    private void ApplyVisibility()
    {
        SetRenderersEnabled(_surfaceRenderer, !_hideSurface);
        SetRenderersEnabled(_entityRenderer, !_hideEntities);
        if (_gameUIDocument != null && _gameUIDocument.rootVisualElement != null)
        {
            DisplayStyle display = _hideGameUI ? DisplayStyle.None : DisplayStyle.Flex;
            if (_gameUIDocument.rootVisualElement.style.display != display)
            {
                _gameUIDocument.rootVisualElement.style.display = display;
            }
        }
    }

    // Перегрузка со списком, а не с массивом. Обход вызывается на каждом
    // кадре отрисовки окна и по разу на слой, а возвращающая массив
    // перегрузка выдаёт новый массив на каждый вызов — мусор ровно там, где
    // человек смотрит на стоимость кадра.
    private readonly List<Renderer> _rendererScratch = new();

    private void SetRenderersEnabled(Component? owner, bool enabled)
    {
        if (owner == null)
        {
            return;
        }

        owner.GetComponentsInChildren(includeInactive: true, _rendererScratch);
        for (int i = 0; i < _rendererScratch.Count; i++)
        {
            Renderer renderer = _rendererScratch[i];
            if (renderer.enabled != enabled)
            {
                renderer.enabled = enabled;
            }
        }

        // Список держит ссылки на рендереры до следующего вызова; по слою в
        // кадре этого достаточно, чтобы уничтоженные объекты не залёживались.
        _rendererScratch.Clear();
    }

    private void DrawBypassWarning()
    {
        int active = 0;
        if (_debugSettings.BypassLightingCompute)
        {
            active++;
        }

        if (_debugSettings.BypassTerrainDraw)
        {
            active++;
        }

        if (_debugSettings.BypassCpuMeshRebuild)
        {
            active++;
        }

        if (PostProcessRuntimeState.SkipPasses)
        {
            active++;
        }

        if (PostProcessRuntimeState.BypassPostProcessEffects)
        {
            active++;
        }

        active += (_hideSurface ? 1 : 0) + (_hideEntities ? 1 : 0) + (_hideGameUI ? 1 : 0);

        if (active == 0)
        {
            return;
        }

        if (_bypassBannerCount != active)
        {
            _bypassBannerCount = active;
            _bypassBanner = $"КАДР НЕПОЛНЫЙ · ОБХОДОВ: {active}";
        }

        ToolChrome.Banner(_bypassBanner, ToolTheme.Error);
        GUILayout.Space(4f);
    }


    private void DrawLightingViewPicker(LightingEngine lighting)
    {
        ToolChrome.SectionHeader("ВИД ОСВЕЩЕНИЯ");
        bool custom = lighting.ActiveDebugView != LightingEngine.DebugView.FinalLighting;

        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(custom ? ToolTheme.Warning : ToolTheme.Success);
            LightingEngine.DebugView view = lighting.ActiveDebugView;
            if (_lightingViewLabelValue != view)
            {
                _lightingViewLabelValue = view;
                _lightingViewLabel = view.ToString();
            }

            GUILayout.Label(_lightingViewLabel, MutedLabelStyle);
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("◄", SecondaryButtonStyle, ToolLayout.Width(34f)))
            {
                StepLightingView(lighting, -1);
            }

            if (GUILayout.Button("Следующий вид", ActiveButtonStyle))
            {
                StepLightingView(lighting, 1);
            }
        }

        bool controlsEnabled = GUI.enabled;
        GUI.enabled = controlsEnabled && custom;
        if (GUILayout.Button("Вернуть обычный", SecondaryButtonStyle))
        {
            lighting.SetDebugView(LightingEngine.DebugView.FinalLighting);
        }

        GUI.enabled = controlsEnabled;
    }

    private static bool DrawSwitch(bool value, string label, Color? activeColor = null)
    {
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(value
                ? activeColor ?? ToolTheme.Error
                : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            return GUILayout.Toggle(value, label, ToolTheme.SegmentedButton);
        }
    }

    private static void StepLightingView(LightingEngine lighting, int step)
    {
        int total = System.Enum.GetValues(typeof(LightingEngine.DebugView)).Length;

        // Плюс длина перед остатком: в C# остаток отрицательного числа
        // отрицателен, и шаг назад с нулевого вида дал бы недопустимый вид.
        int next = ((int)lighting.ActiveDebugView + step + total) % total;
        lighting.SetDebugView((LightingEngine.DebugView)next);
    }

    // Переключается флаг возможности, а не яркость. Яркость выведена из общей
    // экспозиции сцены и обнулять её здесь значило бы тихо ломать экспозицию;
    // флаг же смотрит LightingFrameExecutor каждый кадр, то есть проход
    // динамического света действительно перестаёт считаться.
    //
    // Раньше метод был пуст: переключатель рисовался, щёлкал и не делал
    // ничего — комментарий «константа, не настраивается» описывал тупик,
    // а не решение.
    public void ToggleDynamicLight()
    {
        LightingConfigHolder.EnabledFeatures = LightingConfigHolder.DynamicLightEnabled
            ? LightingConfigHolder.EnabledFeatures & ~LightingFeatureFlags.DynamicLights
            : LightingConfigHolder.EnabledFeatures | LightingFeatureFlags.DynamicLights;

        // Снятый флаг только отключает расчёт прохода — уже посчитанное
        // динамическое излучение осталось бы висеть в поле, и свет не погас
        // бы до первого чужого сброса кэша.
        _lighting?.InvalidateRadiance();
    }
}
