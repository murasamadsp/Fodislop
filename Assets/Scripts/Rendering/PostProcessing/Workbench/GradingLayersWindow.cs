#nullable enable

using System.Collections.Generic;
using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

/// <summary>
/// Интерактивное рабочее место колориста с поддержкой быстрого переключения слоёв,
/// табов, клавиатурных хоткеев и соло/обхода без схлопывания разметки.
/// </summary>
internal sealed class GradingLayersWindow : ToolWindow
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private readonly GradingLayerControlsDrawer _drawer;
    private Vector2 _scroll;
    private ColorGradeLayer? _selectedLayer = ColorGradeLayer.Exposure;
    private ColorGradeLayer? _selectedLayerRequested;
    private bool _selectionRequested;

    public GradingLayersWindow(ColorGradeState state, ColorGradeZones zones)
        : base("Тонкоррекция  ·  F5", new Rect(292f, 16f, 430f, 740f))
    {
        _state = state;
        _zones = zones;
        _drawer = new GradingLayerControlsDrawer(state, zones);
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(400f, 420f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _drawer.ResetState();
        _selectedLayer = ColorGradeLayer.Exposure;
        _selectedLayerRequested = null;
        _selectionRequested = false;
    }

    protected override void DrawContent()
    {
        ApplyPendingSelection();
        HandleKeyboardShortcuts();
        _drawer.ApplyPendingActions();
        DrawLayerTabBar();
        DrawMasterStatusBanners();

        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            if (_selectedLayer.HasValue)
            {
                DrawFocusedLayer(_selectedLayer.Value);
            }
            else
            {
                DrawAllLayers();
            }

            _drawer.DrawActions(SectionLabelStyle, WrappedLabelStyle);
        }
    }

    private void HandleKeyboardShortcuts()
    {
        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown ||
            GUIUtility.keyboardControl != 0 ||
            !ToolWindows.IsFocused(this))
        {
            return;
        }

        switch (currentEvent.keyCode)
        {
            case KeyCode.Alpha1:
                RequestLayer(ColorGradeLayer.Exposure);
                currentEvent.Use();
                break;

            case KeyCode.Alpha2:
                RequestLayer(ColorGradeLayer.WhiteBalance);
                currentEvent.Use();
                break;

            case KeyCode.Alpha3:
                RequestLayer(ColorGradeLayer.Cdl);
                currentEvent.Use();
                break;

            case KeyCode.Alpha4:
                RequestLayer(ColorGradeLayer.Saturation);
                currentEvent.Use();
                break;

            case KeyCode.Alpha5:
                RequestLayer(ColorGradeLayer.Contrast);
                currentEvent.Use();
                break;

            case KeyCode.Alpha6:
                RequestLayer(ColorGradeLayer.Curve);
                currentEvent.Use();
                break;

            case KeyCode.Alpha0:
            case KeyCode.BackQuote:
                RequestLayer(null);
                currentEvent.Use();
                break;

            case KeyCode.LeftBracket:
            case KeyCode.LeftArrow:
                SelectPreviousLayer();
                currentEvent.Use();
                break;

            case KeyCode.RightBracket:
            case KeyCode.RightArrow:
                SelectNextLayer();
                currentEvent.Use();
                break;

            case KeyCode.S:
                ToggleSoloCurrentLayer();
                currentEvent.Use();
                break;

            case KeyCode.B:
            case KeyCode.M:
                ToggleBypassCurrentLayer();
                currentEvent.Use();
                break;

            case KeyCode.R:
                ResetCurrentLayer();
                currentEvent.Use();
                break;

            default:
                break;
        }
    }

    private void SelectPreviousLayer()
    {
        if (!_selectedLayer.HasValue)
        {
            RequestLayer(ColorGradeLayer.Curve);
            return;
        }

        int current = (int)_selectedLayer.Value;
        RequestLayer((ColorGradeLayer)((current - 1 + 6) % 6));
    }

    private void SelectNextLayer()
    {
        if (!_selectedLayer.HasValue)
        {
            RequestLayer(ColorGradeLayer.Exposure);
            return;
        }

        int current = (int)_selectedLayer.Value;
        RequestLayer((ColorGradeLayer)((current + 1) % 6));
    }

    private void ToggleSoloCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _drawer.RequestSolo(_state.Solo == layer ? null : layer);
    }

    private void ToggleBypassCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _drawer.RequestBypass(layer, !_state.IsBypassed(layer));
    }

    private void ResetCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _state.ResetLayer(layer);
        _drawer.ClearNumberCache();
    }

    private void DrawLayerTabBar()
    {
        GUILayout.Label("СЛОИ КОНВЕЙЕРА", SectionLabelStyle);
        using (new GUILayout.HorizontalScope())
        {
            DrawTabButton(ColorGradeLayer.Exposure, "1  Экспозиция");
            DrawTabButton(ColorGradeLayer.WhiteBalance, "2  Баланс");
            DrawTabButton(ColorGradeLayer.Cdl, "3  CDL");
        }

        using (new GUILayout.HorizontalScope())
        {
            DrawTabButton(ColorGradeLayer.Saturation, "4  Цвет");
            DrawTabButton(ColorGradeLayer.Contrast, "5  Контраст");
            DrawTabButton(ColorGradeLayer.Curve, "6  Кривая");
        }

        bool isAll = !_selectedLayer.HasValue;
        if (GUILayout.Toggle(isAll, "0  Все слои", SegmentedButtonStyle) && !isAll)
        {
            RequestLayer(null);
        }
    }

    private void DrawTabButton(ColorGradeLayer layer, string label)
    {
        bool isSelected = _selectedLayer == layer;
        bool isSolo = _state.Solo == layer;
        bool isBypassed = _state.IsBypassed(layer);

        string badge = isSolo ? "★" : (isBypassed ? "○" : "●");
        string title = $"{badge}{label}";

        if (GUILayout.Toggle(
                isSelected,
                title,
                SegmentedButtonStyle,
                GUILayout.ExpandWidth(true)) &&
            !isSelected)
        {
            RequestLayer(layer);
        }
    }

    private void DrawMasterStatusBanners()
    {
        if (_state.Solo.HasValue)
        {
            using (new GUILayout.VerticalScope(CardStyle))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(
                        $"★  Соло: {GradingLayerControlsDrawer.GetLayerTitle(_state.Solo.Value)}",
                        ToolTheme.WarningLabel);
                    if (GUILayout.Button("Снять", SecondaryButtonStyle, GUILayout.Width(72f)))
                    {
                        _drawer.RequestSolo(null);
                    }
                }
            }
        }

        int bypassedCount = GetBypassedCount();
        if (bypassedCount > 0)
        {
            using (new GUILayout.VerticalScope(CardStyle))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label($"⚠  В обходе слоёв: {bypassedCount}", ToolTheme.WarningLabel);
                    if (GUILayout.Button("Включить все", SecondaryButtonStyle, GUILayout.Width(104f)))
                    {
                        _drawer.RequestClearBypasses();
                    }
                }
            }
        }
    }

    private int GetBypassedCount()
    {
        int count = 0;
        for (int i = 0; i < 6; i++)
        {
            if (_state.IsBypassed((ColorGradeLayer)i))
            {
                count++;
            }
        }

        return count;
    }

    private void DrawFocusedLayer(ColorGradeLayer layer)
    {
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("◄  Предыдущий", SecondaryButtonStyle, GUILayout.Width(112f)))
            {
                SelectPreviousLayer();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                $"СЛОЙ: {GradingLayerControlsDrawer.GetLayerTitle(layer).ToUpperInvariant()}",
                SectionLabelStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Следующий  ►", SecondaryButtonStyle, GUILayout.Width(112f)))
            {
                SelectNextLayer();
            }
        }

        using (new GUILayout.VerticalScope(CardStyle))
        {
            DrawLayerHeaderBar(layer, GradingLayerControlsDrawer.GetLayerTitle(layer), showFocusButton: false);
            GUILayout.Space(4f);
            _drawer.DrawLayerControls(layer);
        }

        GUILayout.Label(
            "Горячие клавиши: 1-6 — выбор слоя, 0 — все, [ / ] — перелистывание, B — обход, S — соло, R — сброс.",
            MutedLabelStyle);
    }

    private void DrawAllLayers()
    {
        GUILayout.Label("ЛИНЕЙНОЕ ПРОСТРАНСТВО  ·  ФИЗИКА СЪЁМКИ", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Exposure, "Экспозиция");
        DrawLayerSection(ColorGradeLayer.WhiteBalance, "Баланс белого");

        GUILayout.Space(8f);
        GUILayout.Label("ЛОГАРИФМИЧЕСКОЕ  ·  ТВОРЧЕСКИЙ ГРЕЙД", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Cdl, "ASC CDL");
        DrawLayerSection(ColorGradeLayer.Saturation, "Насыщенность");
        DrawLayerSection(ColorGradeLayer.Contrast, "Контраст");

        GUILayout.Space(8f);
        GUILayout.Label("КРИВАЯ ВЫВОДА", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Curve, "Кривая");
    }

    private void DrawLayerSection(ColorGradeLayer layer, string title)
    {
        using (new GUILayout.VerticalScope(CardStyle))
        {
            DrawLayerHeaderBar(layer, title, showFocusButton: true);
            GUILayout.Space(4f);
            _drawer.DrawLayerControls(layer);
        }
    }

    private void DrawLayerHeaderBar(ColorGradeLayer layer, string title, bool showFocusButton)
    {
        bool active = _state.IsActive(layer);
        bool soloed = _state.Solo == layer;
        bool wasBypassed = _state.IsBypassed(layer);

        using (new GUILayout.HorizontalScope())
        {
            string statusIcon = soloed ? "★ " : (active ? "● " : "○ ");
            GUILayout.Label(statusIcon + title, SectionLabelStyle);
            GUILayout.FlexibleSpace();
            if (showFocusButton && GUILayout.Button("Открыть слой", ActiveButtonStyle, GUILayout.Width(96f)))
            {
                RequestLayer(layer);
            }
        }

        using (new GUILayout.HorizontalScope())
        {
            bool controlsEnabled = GUI.enabled;
            GUI.enabled = controlsEnabled && !soloed;
            bool bypass = GUILayout.Toggle(
                wasBypassed,
                "B  Обход",
                SegmentedButtonStyle,
                GUILayout.ExpandWidth(true));
            GUI.enabled = controlsEnabled;
            if (bypass != wasBypassed)
            {
                _drawer.RequestBypass(layer, bypass);
            }

            bool solo = GUILayout.Toggle(
                soloed,
                "S  Соло",
                SegmentedButtonStyle,
                GUILayout.ExpandWidth(true));
            if (solo != soloed)
            {
                _drawer.RequestSolo(solo ? layer : null);
            }

            if (GUILayout.Button("R  Сброс", SecondaryButtonStyle, GUILayout.Width(84f)))
            {
                _state.ResetLayer(layer);
                _drawer.ClearNumberCache();
            }
        }
    }

    private void RequestLayer(ColorGradeLayer? layer)
    {
        _selectedLayerRequested = layer;
        _selectionRequested = true;
    }

    private void ApplyPendingSelection()
    {
        if (!_selectionRequested || Event.current.type != EventType.Layout)
        {
            return;
        }

        _selectedLayer = _selectedLayerRequested;
        _selectedLayerRequested = null;
        _selectionRequested = false;
    }
}
