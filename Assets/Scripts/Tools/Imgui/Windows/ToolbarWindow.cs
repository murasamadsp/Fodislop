#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

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

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 260f);

    protected override bool CanClose => false;

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _labels.Clear();
        _labelSignature = 0;
    }

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
            signature = (signature * 31) + window.ID;
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
        using (ToolLayout.Horizontal())
        {
            GUILayout.Label("Масштаб", ToolTheme.FieldLabel);
            if (GUILayout.Button("−", ToolLayout.Width(30f)))
            {
                ToolWindows.RequestScale(ToolWindows.Scale - 0.25f);
            }

            GUILayout.Label(_scaleLabel, ToolLayout.Width(46f));
            if (GUILayout.Button("+", ToolLayout.Width(30f)))
            {
                ToolWindows.RequestScale(ToolWindows.Scale + 0.25f);
            }
        }

        using (ToolLayout.ScrollView(ref _scroll))
        {
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

    private void DrawWindowRow(ToolWindow window)
    {
        using (ToolLayout.Horizontal())
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
