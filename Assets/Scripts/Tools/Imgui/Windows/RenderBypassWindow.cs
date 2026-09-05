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
    private const float DefaultDynamicLightIntensity = 1.25f;

    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly LightingEngine? _lighting;
    private readonly WorldGizmoOptions _gizmos;
    private float _rememberedDynamicLightIntensity = DefaultDynamicLightIntensity;
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
        _rememberedDynamicLightIntensity = DefaultDynamicLightIntensity;
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
            GUILayout.Label("ОБХОДЫ", SectionLabelStyle);
            GUILayout.Label("Активный пункт отключает соответствующий этап.", MutedLabelStyle);
            _debugSettings.BypassLightingCompute = DrawSwitch(
                _debugSettings.BypassLightingCompute, "Расчёт освещения");
            _debugSettings.BypassTerrainDraw = DrawSwitch(
                _debugSettings.BypassTerrainDraw, "Отрисовка террейна");
            _debugSettings.BypassCpuMeshRebuild = DrawSwitch(
                _debugSettings.BypassCpuMeshRebuild, "Пересборка меша");
            _debugSettings.ShowRobotDebugVisuals = DrawSwitch(
                _debugSettings.ShowRobotDebugVisuals, "Отладка роботов");

            ToolTheme.Separator();
            GUILayout.Label("ГИЗМО В МИРЕ", SectionLabelStyle);
            _gizmos.ShowGrid = DrawSwitch(_gizmos.ShowGrid, "Сетка чанков");
            _gizmos.ShowCursor = DrawSwitch(_gizmos.ShowCursor, "Курсор клетки");

            if (_lighting == null)
            {
                return;
            }

            ToolTheme.Separator();
            GUILayout.Label("ДИНАМИЧЕСКИЙ СВЕТ", SectionLabelStyle);
            bool lit = _lighting.DynamicLightIntensity > 0.01f;
            if (DrawSwitch(lit, "Динамический свет") != lit)
            {
                ToggleDynamicLight();
            }

            GUILayout.Label("ВИД ОСВЕЩЕНИЯ", SectionLabelStyle);
            GUILayout.Label(_lighting.ActiveDebugView.ToString(), MetricLabelStyle);
            if (GUILayout.Button("Следующий вид", ActiveButtonStyle))
            {
                CycleLightingView(_lighting);
            }

            bool controlsEnabled = GUI.enabled;
            GUI.enabled = controlsEnabled &&
                _lighting.ActiveDebugView != LightingEngine.DebugView.FinalLighting;
            if (GUILayout.Button("Вернуть обычный", SecondaryButtonStyle))
            {
                _lighting.SetDebugView(LightingEngine.DebugView.FinalLighting);
            }

            GUI.enabled = controlsEnabled;
        }
    }

    private static bool DrawSwitch(bool value, string label)
    {
        string marker = value ? "●" : "○";
        return GUILayout.Toggle(value, $"{marker}  {label}", ToolTheme.SegmentedButton);
    }

    /// <summary>
    /// Следующий вид по кругу. Длина берётся из самого перечисления: список
    /// уже рос, и зашитое число молча отрезало бы новые виды.
    /// </summary>
    public static void CycleLightingView(LightingEngine lighting)
    {
        int total = System.Enum.GetValues(typeof(LightingEngine.DebugView)).Length;
        int next = ((int)lighting.ActiveDebugView + 1) % total;
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

        float current = _lighting.DynamicLightIntensity;
        if (current > 0.01f)
        {
            _rememberedDynamicLightIntensity = current;
            _lighting.SetDynamicLightSettings(0f, _lighting.DynamicLightColor);
            return;
        }

        _lighting.SetDynamicLightSettings(
            Mathf.Max(_rememberedDynamicLightIntensity, 0.01f),
            _lighting.DynamicLightColor);
    }
}
