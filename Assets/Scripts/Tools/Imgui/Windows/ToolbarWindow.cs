#nullable enable

using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

/// <summary>
/// Список всех инструментов: что есть и что открыто.
/// </summary>
/// <remarks>
/// Раньше набор отладочных возможностей знали только те, кто помнил клавиши:
/// цифры от одного до восьми, часть с дублем на F-клавишах, нигде не
/// перечисленные. Инструмент, о котором нельзя узнать иначе как из кода, —
/// это инструмент, которым не пользуются.
/// </remarks>
public sealed class ToolbarWindow : ToolWindow
{
    private Vector2 _scroll;

    public ToolbarWindow()
        : base("Инструменты  ·  F1", new Rect(16f, 16f, 260f, 350f))
    {
        Visible = true;
    }

    /// <summary>Сам список данных не собирает.</summary>
    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 260f);

    protected override bool CanClose => false;

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
    }

    protected override void DrawContent()
    {
        ToolChrome.SectionHeader("РАБОЧЕЕ ПРОСТРАНСТВО");
        GUILayout.Label(
            "Открывайте только нужные панели — состояние окон сохраняется при скрытии интерфейса.",
            MutedLabelStyle);
        GUILayout.Space(4f);
        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            foreach (ToolWindow window in ToolWindows.All)
            {
                if (ReferenceEquals(window, this))
                {
                    continue;
                }

                DrawWindowRow(window);
            }

            ToolTheme.Separator();
            if (GUILayout.Button("Сбросить расположение", SecondaryButtonStyle))
            {
                ToolWindows.ResetLayout();
            }

            ToolChrome.SectionHeader("КЛАВИШИ");
            GUILayout.Label("F1  —  скрыть или показать все инструменты", MutedLabelStyle);
        }
    }

    /// <summary>
    /// Строка одного инструмента: точка состояния, название, тумблер.
    /// </summary>
    /// <remarks>
    /// Точек две разных, и это не украшение. Жёлтая — окно открыто. Синяя —
    /// окно закрыто, но продолжает копить данные: у части инструментов история
    /// набирается всегда, и без этой отметки закрытое окно выглядело бы
    /// выключенным, хотя оно работает.
    /// </remarks>
    private static void DrawWindowRow(ToolWindow window)
    {
        using (new GUILayout.HorizontalScope())
        {
            Color pip = window.Visible
                ? ToolPalette.Accent
                : window.WantsSampling
                    ? ToolPalette.Data
                    : ToolPalette.Fade(ToolPalette.MutedText, 0.5f);
            ToolChrome.StatusPip(pip);

            bool visible = GUILayout.Toggle(
                window.Visible,
                window.DisplayTitle,
                SegmentedButtonStyle);
            if (visible == window.Visible)
            {
                return;
            }

            ToolWindows.RequestVisibility(window, visible);
            if (visible)
            {
                ToolWindows.RequestFocus(window);
            }
        }
    }
}
