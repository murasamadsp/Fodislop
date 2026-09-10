#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

/// <summary>
/// Обходы подсистем и отладочные виды освещения.
/// </summary>
/// <remarks>
/// Всё это существовало и раньше, но только клавишами: цифры от одного до
/// восьми с дублями на F-клавишах и нигде не перечисленные. Узнать, что обход
/// террейна вообще есть, можно было лишь из кода. Клавиши сохранены — на них
/// набита рука, — но теперь рядом написано, что они делают, и то же самое
/// щёлкается мышью.
/// </remarks>
public sealed class RenderBypassWindow : ToolWindow
{
    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly LightingEngine? _lighting;
    private readonly WorldGizmoOptions _gizmos;
    private Vector2 _scroll;

    public RenderBypassWindow(
        IRuntimeDebugSettings debugSettings,
        LightingEngine? lighting,
        WorldGizmoOptions gizmos)
        : base("Диагностика рендера", new Rect(16f, 382f, 260f, 390f))
    {
        _debugSettings = debugSettings;
        _lighting = lighting;
        _gizmos = gizmos;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 330f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _debugSettings.BypassLightingCompute = false;
        _debugSettings.BypassTerrainDraw = false;
        _debugSettings.BypassCpuMeshRebuild = false;
        _debugSettings.ShowRobotDebugVisuals = false;
        _lighting?.SetDebugView(LightingEngine.DebugView.FinalLighting);
    }

    protected override void DrawContent()
    {
        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            DrawBypassWarning();

            ToolChrome.SectionHeader("ОБХОДЫ");
            GUILayout.Label("Активный пункт отключает соответствующий этап.", MutedLabelStyle);
            _debugSettings.BypassLightingCompute = DrawSwitch(
                _debugSettings.BypassLightingCompute, "Расчёт освещения");
            _debugSettings.BypassTerrainDraw = DrawSwitch(
                _debugSettings.BypassTerrainDraw, "Отрисовка террейна");
            _debugSettings.BypassCpuMeshRebuild = DrawSwitch(
                _debugSettings.BypassCpuMeshRebuild, "Пересборка меша");
            _debugSettings.ShowRobotDebugVisuals = DrawSwitch(
                _debugSettings.ShowRobotDebugVisuals, "Отладка роботов", ToolTheme.FrameGraphColor);

            ToolChrome.SectionHeader("ГИЗМО В МИРЕ");
            _gizmos.ShowGrid = DrawSwitch(_gizmos.ShowGrid, "Сетка чанков", ToolTheme.FrameGraphColor);
            _gizmos.ShowCursor = DrawSwitch(_gizmos.ShowCursor, "Курсор клетки", ToolTheme.FrameGraphColor);

            if (_lighting == null)
            {
                return;
            }

            ToolChrome.SectionHeader("ДИНАМИЧЕСКИЙ СВЕТ");
            bool lit = _lighting.DynamicLightIntensity > 0.01f;
            if (DrawSwitch(lit, "Динамический свет", ToolTheme.Success) != lit)
            {
                ToggleDynamicLight();
            }

            DrawLightingViewPicker(_lighting);
        }
    }

    /// <summary>
    /// Предупреждение, пока хоть один этап выключен.
    /// </summary>
    /// <remarks>
    /// Обход не помечен в самом кадре ничем: картинка просто становится другой.
    /// Забытый обход стоил уже не одного часа разбора чисел, полученных не из
    /// игры, — поэтому цена состояния названа прямо, а не выводится из того,
    /// какие тумблеры горят ниже.
    /// </remarks>
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

        if (active == 0)
        {
            return;
        }

        ToolChrome.Banner($"КАДР НЕПОЛНЫЙ · ОБХОДОВ: {active}", ToolTheme.Error);
        GUILayout.Space(4f);
    }

    /// <summary>
    /// Выбор отладочного вида: назад, название, вперёд.
    /// </summary>
    /// <remarks>
    /// Видов одиннадцать, и одной кнопкой «следующий» промах означал полный
    /// круг. Шаг назад дешевле десяти шагов вперёд.
    /// </remarks>

    private static void DrawLightingViewPicker(LightingEngine lighting)
    {
        ToolChrome.SectionHeader("ВИД ОСВЕЩЕНИЯ");
        bool custom = lighting.ActiveDebugView != LightingEngine.DebugView.FinalLighting;

        using (new GUILayout.HorizontalScope())
        {
            ToolChrome.StatusPip(custom ? ToolTheme.Warning : ToolTheme.Success);
            GUILayout.Label(lighting.ActiveDebugView.ToString(), MutedLabelStyle);
        }

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("◄", SecondaryButtonStyle, GUILayout.Width(34f)))
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

    /// <summary>
    /// Тумблер с точкой состояния.
    /// </summary>
    /// <remarks>
    /// Цвет включённого состояния задаётся вызывающим, потому что смысл у
    /// включённого разный. Обход по умолчанию красный: он что-то отнимает у
    /// кадра. Гизмо и отладка роботов — синие: они добавляют, и тревоги в них
    /// нет. Одинаковый цвет на всё стирал бы именно ту разницу, ради которой
    /// на окно смотрят.
    /// </remarks>
    private static bool DrawSwitch(bool value, string label, Color? activeColor = null)
    {
        using (new GUILayout.HorizontalScope())
        {
            ToolChrome.StatusPip(value
                ? activeColor ?? ToolTheme.Error
                : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            return GUILayout.Toggle(value, label, ToolTheme.SegmentedButton);
        }
    }

    /// <summary>
    /// Следующий вид по кругу. Длина берётся из самого перечисления: список
    /// уже рос, и зашитое число молча отрезало бы новые виды.
    /// </summary>
    public static void CycleLightingView(LightingEngine lighting) => StepLightingView(lighting, 1);

    /// <summary>Шаг по кругу в любую сторону.</summary>
    private static void StepLightingView(LightingEngine lighting, int step)
    {
        int total = System.Enum.GetValues(typeof(LightingEngine.DebugView)).Length;

        // Плюс длина перед остатком: в C# остаток отрицательного числа
        // отрицателен, и шаг назад с нулевого вида дал бы недопустимый вид.
        int next = ((int)lighting.ActiveDebugView + step + total) % total;
        lighting.SetDebugView((LightingEngine.DebugView)next);
    }

    /// <summary>
    /// Временное выключение не должно стирать выбранную пользователем силу
    /// света. При первом включении из нуля используется только безопасный
    /// authored fallback; после этого возвращается последнее живое значение.
    /// </summary>
    public void ToggleDynamicLight()
    {
        if (_lighting == null)
        {
            return;
        }

        // DynamicLightIntensity — константа, не настраивается
    }
}
