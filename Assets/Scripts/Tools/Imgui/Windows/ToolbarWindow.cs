#nullable enable

using System.Collections.Generic;
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
    private readonly Dictionary<ToolWindow, string> _labels = [];
    private int _labelSignature;
    private Vector2 _scroll;
    private float _labelScale = -1f;
    private string _scaleLabel = string.Empty;

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
        _labels.Clear();
        _labelSignature = 0;
    }

    /// <summary>
    /// Пересобирает подписи, только когда они действительно изменились.
    /// </summary>
    /// <remarks>
    /// Подпись строки — это склейка номера, названия и пометки о свёрнутости.
    /// Собирать её в <c>DrawContent</c> значило бы делать это по нескольку раз
    /// за кадр на каждое окно: IMGUI проходит раскладку и отрисовку разными
    /// событиями. Мусор в списке инструментов особенно неуместен — рядом стоит
    /// окно, которое этот мусор показывает.
    ///
    /// Отпечаток дешёвый и намеренно грубый: в нём номер окна и его
    /// свёрнутость, то есть ровно то, от чего подпись зависит.
    /// </remarks>
    public override void Tick()
    {
        if (_labelScale != ToolWindows.Scale)
        {
            _labelScale = ToolWindows.Scale;
            _scaleLabel = $"{_labelScale * 100f:0}%";
        }

        int signature = 17;
        foreach (ToolWindow window in ToolWindows.All)
        {
            signature = (signature * 31) + window.Id;
            signature = (signature * 31) + (window.Collapsed ? 1 : 0);
        }

        if (signature == _labelSignature)
        {
            return;
        }

        _labelSignature = signature;
        _labels.Clear();
        foreach (ToolWindow window in ToolWindows.All)
        {
            if (ReferenceEquals(window, this))
            {
                continue;
            }

            _labels[window] = window.Collapsed
                ? window.DisplayTitle + "   (свёрнуто)"
                : window.DisplayTitle;
        }
    }

    protected override void DrawContent()
    {
        ToolChrome.SectionHeader("РАБОЧЕЕ ПРОСТРАНСТВО");
        GUILayout.Label(
            "Открывайте только нужные панели — состояние окон сохраняется при скрытии интерфейса.",
            MutedLabelStyle);
        GUILayout.Space(4f);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Масштаб", ToolTheme.FieldLabel);
            if (GUILayout.Button("−", GUILayout.Width(30f)))
            {
                ToolWindows.RequestScale(ToolWindows.Scale - 0.25f);
            }

            GUILayout.Label(_scaleLabel, GUILayout.Width(46f));
            if (GUILayout.Button("+", GUILayout.Width(30f)))
            {
                ToolWindows.RequestScale(ToolWindows.Scale + 0.25f);
            }
        }

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
            GUILayout.Label("Esc  —  вернуть управление игре из поля ввода", MutedLabelStyle);
            GUILayout.Label("−  —  свернуть окно в полосу заголовка", MutedLabelStyle);
            ToolTheme.Separator();
            GUILayout.Label(
                "Расположение и состав окон запоминаются между запусками. " +
                "«Сбросить расположение» стирает и запомненное.",
                MutedLabelStyle);
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
    private void DrawWindowRow(ToolWindow window)
    {
        using (new GUILayout.HorizontalScope())
        {
            Color pip = window.Visible
                ? ToolPalette.Accent
                : window.WantsSampling
                    ? ToolPalette.Data
                    : ToolPalette.Fade(ToolPalette.MutedText, 0.5f);
            ToolChrome.StatusPip(pip);

            if (!_labels.TryGetValue(window, out string? label))
            {
                label = window.DisplayTitle;
            }

            bool visible = GUILayout.Toggle(window.Visible, label, SegmentedButtonStyle);
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
