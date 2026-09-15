#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

internal sealed class GradingLayerControlsDrawer
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private readonly Dictionary<string, string> _numberText = [];

    // Имена контролов и id каналов выводятся из id слайдера. Склейка на
    // каждое событие IMGUI давала десятки строк за кадр на одно окно.
    private readonly Dictionary<string, string> _controlNames = [];
    private readonly Dictionary<string, string[]> _channelIds = [];

    private static readonly GUILayoutOption _SliderLabelWidth = GUILayout.Width(122f);
    private static readonly GUILayoutOption _SliderFieldWidth = GUILayout.Width(64f);

    // GUI.GetNameOfFocusedControl() каждый раз возвращает новую строку из
    // нативного кода, а вызывался он в каждом слайдере в каждом событии.
    // Имя меняется только вместе с фокусом клавиатуры.
    private int _focusedNameKeyboardControl = int.MinValue;
    private string _focusedName = string.Empty;

    private string FocusedControlName()
    {
        int keyboardControl = GUIUtility.keyboardControl;
        if (keyboardControl != _focusedNameKeyboardControl)
        {
            _focusedNameKeyboardControl = keyboardControl;
            _focusedName = GUI.GetNameOfFocusedControl();
        }

        return _focusedName;
    }
    private string? _status;
    private string? _invalidNumberId;
    private bool _statusIsError;
    private ColorGradeLayer? _bypassLayerRequested;
    private bool _bypassValueRequested;
    private bool _soloChangeRequested;
    private ColorGradeLayer? _soloRequested;
    private bool _clearPreviewRequested;
    private bool _clearBypassesRequested;
    private bool _loadRequested;
    private bool _loadPresetRequested;
    private bool _resetAllRequested;
    private ColorGradeCurve? _selectedCurve;
    private int _selectedCurvePoint = -1;
    private bool _draggingCurvePoint;
    private string _presetName = "default";
    private static Texture2D? _wheelTexture;

    public GradingLayerControlsDrawer(ColorGradeState state, ColorGradeZones zones)
    {
        _state = state;
        _zones = zones;
    }

    public string? Status => _status;

    public bool StatusIsError => _statusIsError;

    public void ResetState()
    {
        ReleaseWheelTexture();
        _numberText.Clear();
        _status = null;
        _invalidNumberId = null;
        _statusIsError = false;
        _bypassLayerRequested = null;
        _bypassValueRequested = false;
        _soloChangeRequested = false;
        _soloRequested = null;
        _clearPreviewRequested = false;
        _clearBypassesRequested = false;
        _loadRequested = false;
        _loadPresetRequested = false;
        _resetAllRequested = false;
        _presetName = "default";
        _selectedCurve = null;
        _selectedCurvePoint = -1;
        _draggingCurvePoint = false;
        _shownCurve = null;
        _shownCurvePoint = -1;
    }

    public void ClearNumberCache()
    {
        _numberText.Clear();
    }

    public void RequestBypass(ColorGradeLayer layer, bool bypass)
    {
        _bypassLayerRequested = layer;
        _bypassValueRequested = bypass;
    }

    public void RequestSolo(ColorGradeLayer? layer)
    {
        _soloChangeRequested = true;
        _soloRequested = layer;
    }

    public void RequestClearBypasses()
    {
        _clearBypassesRequested = true;
    }

    public void SetStatus(bool success, string successMessage, string failureMessage)
    {
        _invalidNumberId = null;
        _statusIsError = !success;
        _status = success ? successMessage : failureMessage;
    }

    public void DrawLayerControls(ColorGradeLayer layer)
    {
        bool active = _state.IsActive(layer);
        bool previousGuiEnabled = GUI.enabled;
        if (!active)
        {
            GUI.enabled = false;
        }

        switch (layer)
        {
            case ColorGradeLayer.Exposure:
                DrawExposureControls();
                break;

            case ColorGradeLayer.WhiteBalance:
                DrawWhiteBalanceControls();
                break;

            case ColorGradeLayer.Cdl:
                DrawCdlControls();
                break;

            case ColorGradeLayer.Saturation:
                DrawSaturationControls();
                break;

            case ColorGradeLayer.Contrast:
                DrawContrastControls();
                break;

            case ColorGradeLayer.Curve:
                DrawCurveControls();
                break;

            default:
                break;
        }

        GUI.enabled = previousGuiEnabled;
        if (!active)
        {
            string reason = _state.Solo.HasValue
                ? $"Слой выключен (активно соло другого слоя: {GetLayerTitle(_state.Solo.Value)})"
                : "Слой в обходе — значения не влияют на кадр";
            GUILayout.Label(reason, ToolTheme.WarningLabel);
        }
    }

    private void DrawExposureControls()
    {
        _state.Exposure = Slider(
            "exposure", "стопы", _state.Exposure,
            ColorGradeState.ExposureMin, ColorGradeState.ExposureMax);
    }

    private void DrawWhiteBalanceControls()
    {
        if (GUILayout.Button("Eyedropper: neutral white/gray", ToolTheme.SecondaryButton))
        {
            ColorGradeScreenSampler.Arm(sample =>
            {
                float red = Mathf.Max(sample.r, 1e-4f);
                float green = Mathf.Max(sample.g, 1e-4f);
                float blue = Mathf.Max(sample.b, 1e-4f);
                _state.Temperature = Mathf.Clamp((blue - red) * -180f, -100f, 100f);
                _state.Tint = Mathf.Clamp(
                    (green - (red + blue) * 0.5f) * -220f,
                    -100f,
                    100f);
                _numberText.Remove("temperature");
                _numberText.Remove("tint");
            });
        }

        _state.Temperature = Slider(
            "temperature", "температура", _state.Temperature,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);
        _state.Tint = Slider(
            "tint", "оттенок", _state.Tint,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);

        GUILayout.Label("PRIMARY COLOR WHEELS", ToolTheme.SectionLabel);
        Vector3 lift = TripletSlider(
            "primary.lift", "Lift", _state.PrimaryLift,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        DrawPrimaryWheel("LIFT WHEEL", ref lift, Vector3.zero, -0.5f, 0.5f, "primary.lift.wheel");
        _state.PrimaryLift = lift;

        Vector3 gamma = TripletSlider(
            "primary.gamma", "Gamma", _state.PrimaryGamma,
            ColorGradeState.PowerMin, ColorGradeState.PowerMax);
        DrawPrimaryWheel("GAMMA WHEEL", ref gamma, Vector3.one, 0.1f, 4f, "primary.gamma.wheel");
        _state.PrimaryGamma = gamma;

        Vector3 gain = TripletSlider(
            "primary.gain", "Gain", _state.PrimaryGain,
            ColorGradeState.SlopeMin, ColorGradeState.SlopeMax);
        DrawPrimaryWheel("GAIN WHEEL", ref gain, Vector3.one, 0f, 4f, "primary.gain.wheel");
        _state.PrimaryGain = gain;

        _state.PrimaryOffset = TripletSlider(
            "primary.offset", "Offset", _state.PrimaryOffset,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        Vector4 primaryMaster = _state.PrimaryMaster;
        primaryMaster.x = Slider("primary.master.lift", "  Lift master", primaryMaster.x, -0.5f, 0.5f);
        primaryMaster.y = Slider("primary.master.gamma", "  Gamma master", primaryMaster.y, 0.1f, 4f);
        primaryMaster.z = Slider("primary.master.gain", "  Gain master", primaryMaster.z, 0f, 4f);
        primaryMaster.w = Slider("primary.master.offset", "  Offset master", primaryMaster.w, -0.5f, 0.5f);
        _state.PrimaryMaster = primaryMaster;
    }

    private void DrawCdlControls()
    {
        _state.CdlSaturation = Slider(
            "cdl.saturation",
            "Saturation",
            _state.CdlSaturation,
            ColorGradeState.CdlSaturationMin,
            ColorGradeState.CdlSaturationMax);
        Vector3 slope = TripletSlider(
            "slope", "Slope (усиление)", _state.Slope,
            ColorGradeState.SlopeMin, ColorGradeState.SlopeMax);
        DrawPrimaryWheel("GAIN WHEEL", ref slope, Vector3.one, 1f, 4f, "cdl.slope.wheel");
        _state.Slope = slope;
        Vector3 offset = TripletSlider(
            "offset", "Offset (подъём)", _state.Offset,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        DrawPrimaryWheel("LIFT WHEEL", ref offset, Vector3.zero, -0.5f, 0.5f, "cdl.offset.wheel");
        _state.Offset = offset;
        Vector3 power = TripletSlider(
            "power", "Power (гамма)", _state.Power,
            ColorGradeState.PowerMin, ColorGradeState.PowerMax);
        DrawPrimaryWheel("GAMMA WHEEL", ref power, Vector3.one, 0.1f, 4f, "cdl.power.wheel");
        _state.Power = power;
        GUILayout.Label("MASTER / LUMA", ToolTheme.SectionLabel);
        Vector3 master = _state.CdlMaster;
        master.x = Slider("master.slope", "  Slope master", master.x, 0f, 4f);
        master.y = Slider("master.offset", "  Offset master", master.y, -0.5f, 0.5f);
        master.z = Slider("master.power", "  Power master", master.z, 0.1f, 4f);
        _state.CdlMaster = master;
    }

    private void DrawSaturationControls()
    {
        _state.Saturation = Slider(
            "saturation", "насыщенность", _state.Saturation,
            ColorGradeState.SaturationMin, ColorGradeState.SaturationMax);
        _state.Vibrance = Slider(
            "vibrance", "vibrance", _state.Vibrance,
            ColorGradeState.VibranceMin, ColorGradeState.VibranceMax);
        _state.Hue = Slider("hue", "hue shift °", _state.Hue, -180f, 180f);

        GUILayout.Label("SELECTIVE CURVES", ToolTheme.SectionLabel);
        DrawCurveEditor("hue-vs-hue.curve", "Hue vs Hue", _state.HueVsHueCurve);
        DrawCurveEditor(
            "hue-vs-saturation.curve",
            "Hue vs Saturation",
            _state.HueVsSaturationCurve);
        DrawCurveEditor(
            "hue-vs-luminance.curve",
            "Hue vs Luminance",
            _state.HueVsLuminanceCurve);
        DrawCurveEditor(
            "luminance-vs-saturation.curve",
            "Luminance vs Saturation",
            _state.LuminanceVsSaturationCurve);
        DrawCurveEditor(
            "saturation-vs-saturation.curve",
            "Saturation vs Saturation",
            _state.SaturationVsSaturationCurve);
        GUILayout.Label(
            "Кривые «X против Y»: линия посередине — без изменений. Выше — больше, " +
            "ниже — меньше: оттенок сдвигается до ±180°, насыщенность и яркость " +
            "умножаются от ×0 до ×2. У кривых по оттенку края связаны — 0° и 360° " +
            "это один цвет; на серые и почти чёрные пиксели они не действуют.",
            ToolTheme.MutedLabel);
    }

    private void DrawContrastControls()
    {
        _state.Contrast = Slider(
            "contrast", "контраст", _state.Contrast,
            ColorGradeState.ContrastMin, ColorGradeState.ContrastMax);
        _state.Pivot = Slider("pivot", "pivot", _state.Pivot, 0.1f, 0.9f);
        _state.Shadows = Slider("shadows", "shadows", _state.Shadows, -0.5f, 0.5f);
        _state.Highlights = Slider("highlights", "highlights", _state.Highlights, -0.5f, 0.5f);
        _state.Blacks = Slider("blacks", "blacks", _state.Blacks, -0.5f, 0.5f);
        _state.Whites = Slider("whites", "whites", _state.Whites, -0.5f, 0.5f);
        _state.Toe = Slider("toe", "toe", _state.Toe, 0f, 1f);
        _state.Shoulder = Slider("shoulder", "shoulder", _state.Shoulder, 0f, 1f);
    }

    private void DrawCurveControls()
    {
        GUILayout.Label("КРИВЫЕ ТОНА", ToolTheme.SectionLabel);
        if (GUILayout.Button(
                _state.Transform == DisplayTransform.None
                    ? "Display transform: None"
                    : "Display transform: Fodinae",
                ToolTheme.SecondaryButton))
        {
            _state.Transform = _state.Transform == DisplayTransform.None
                ? DisplayTransform.Fodinae
                : DisplayTransform.None;
        }

        _state.WhitePoint = Slider("white-point", "white point", _state.WhitePoint, 0.25f, 8f);
        _state.GreyOut = Slider("grey-out", "grey output", _state.GreyOut, 0.05f, 0.5f);
        _state.CurveSlope = Slider("curve-slope", "curve slope", _state.CurveSlope, 0.5f, 2f);
        _state.ToePower = Slider("toe-power", "toe power", _state.ToePower, 1f, 8f);
        _state.ToeStops = Slider("toe-stops", "toe stops", _state.ToeStops, 4f, 20f);
        _state.ShoulderPower = Slider("shoulder-power", "shoulder power", _state.ShoulderPower, 1f, 8f);
        _state.PathToWhiteAmount = Slider("path-to-white", "path to white", _state.PathToWhiteAmount, 0f, 1f);
        _state.PathToWhitePower = Slider("path-power", "path power", _state.PathToWhitePower, 1f, 8f);

        // Слайдер рисуется всегда и только гасится: появление контрола в том же
        // событии, где нажали тумблер, ломает раскладку IMGUI.
        _state.GamutCompressionEnabled = GUILayout.Toggle(
            _state.GamutCompressionEnabled,
            _state.GamutCompressionEnabled ? "●  Сжатие гамута" : "○  Сжатие гамута",
            ToolTheme.SegmentedButton);
        bool gamutControlsEnabled = GUI.enabled;
        GUI.enabled = gamutControlsEnabled && _state.GamutCompressionEnabled;
        _state.GamutCompressionStrength = Slider(
            "gamut-compression",
            "  сила сжатия",
            _state.GamutCompressionStrength,
            ColorGradeState.GamutCompressionStrengthMin,
            ColorGradeState.GamutCompressionStrengthMax);
        GUI.enabled = gamutControlsEnabled;
        DrawCurveEditor("master-curve", "Master / Luma", _state.MasterCurve);
        DrawCurveEditor("red-curve", "Red", _state.RedCurve);
        DrawCurveEditor("green-curve", "Green", _state.GreenCurve);
        DrawCurveEditor("blue-curve", "Blue", _state.BlueCurve);
        GUILayout.Label(
            "ЛКМ по полю добавляет точку, drag двигает её, ПКМ удаляет. " +
            "Крайние точки фиксированы; smooth использует ограниченный smoothstep без overshoot.",
            ToolTheme.MutedLabel);
    }

    private void DrawCurveEditor(string id, string title, ColorGradeCurve curve)
    {
        GUILayout.Label(title, ToolTheme.FieldLabel);
        Rect graph = GUILayoutUtility.GetRect(300f, 150f, ToolLayout.ExpandWidth(true));
        DrawCurveGraph(graph, curve);

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("+ точка", ToolTheme.SecondaryButton, ToolLayout.Width(74f)))
            {
                _selectedCurve = curve;
                // Точка встаёт на кривую: добавление не должно менять её форму.
                _selectedCurvePoint = curve.AddPoint(new Vector2(0.5f, curve.Evaluate(0.5f)));
            }

            if (GUILayout.Button("reset", ToolTheme.SecondaryButton, ToolLayout.Width(58f)))
            {
                curve.Reset();
                if (ReferenceEquals(_selectedCurve, curve))
                {
                    _selectedCurvePoint = -1;
                }
            }

            ColorCurveInterpolation interpolation = curve.Interpolation;
            bool smooth = GUILayout.Toggle(
                interpolation == ColorCurveInterpolation.Smooth,
                "smooth",
                ToolTheme.SegmentedButton);
            curve.Interpolation = smooth
                ? ColorCurveInterpolation.Smooth
                : ColorCurveInterpolation.Linear;
        }

        // Показывать ли слайдеры точки, решается только на Layout. Выбор
        // меняется на MouseDown, и если слайдеры появлялись в том же событии,
        // Repaint видел другое число контролов, чем Layout: IMGUI падал, и
        // окно уходило в экран ошибки.
        if (Event.current.type == EventType.Layout)
        {
            _shownCurve = _selectedCurve;
            _shownCurvePoint = _selectedCurvePoint;
        }

        if (ReferenceEquals(_shownCurve, curve) && _shownCurvePoint >= 0)
        {
            int pointIndex = Mathf.Min(_shownCurvePoint, curve.PointCount - 1);
            Vector2 point = curve.GetPoint(pointIndex);
            Vector2 edited = new(
                Slider(PointSliderId(id, 0), "  X", point.x, 0f, 1f),
                Slider(PointSliderId(id, 1), "  Y", point.y, 0f, 1f));
            if (edited != point)
            {
                curve.SetPoint(pointIndex, edited);
            }
        }
    }

    private ColorGradeCurve? _shownCurve;
    private int _shownCurvePoint = -1;
    private readonly Dictionary<string, string[]> _pointSliderIds = [];

    private string PointSliderId(string id, int axis)
    {
        if (!_pointSliderIds.TryGetValue(id, out string[]? ids))
        {
            ids = [id + ".point.x", id + ".point.y"];
            _pointSliderIds[id] = ids;
        }

        return ids[axis];
    }

    private void DrawCurveGraph(Rect graph, ColorGradeCurve curve)
    {
        if (Event.current.type == EventType.Repaint)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.13f, 1f);
            GUI.DrawTexture(graph, Texture2D.whiteTexture);
            GUI.color = new Color(0.22f, 0.25f, 0.3f, 1f);
            for (int index = 1; index < 4; index++)
            {
                float x = graph.x + graph.width * index / 4f;
                float y = graph.y + graph.height * index / 4f;
                GUI.DrawTexture(new Rect(x, graph.y, 1f, graph.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(graph.x, y, graph.width, 1f), Texture2D.whiteTexture);
            }

            if (curve.Kind != ColorGradeCurveKind.Tone)
            {
                // Нейтраль «без изменений» для кривых «X против Y».
                GUI.color = new Color(0.55f, 0.6f, 0.68f, 1f);
                GUI.DrawTexture(
                    new Rect(graph.x, graph.yMax - ColorGradeCurve.NeutralLevel * graph.height, graph.width, 1f),
                    Texture2D.whiteTexture);
            }

            // Линия — вертикальные столбики по пикселю ширины, без поворота.
            // Повёрнутые через RotateAroundPivot полоски IMGUI не обрезает
            // окном и прокруткой при масштабированной матрице: кривая рисовалась
            // поверх соседних окон.
            GUI.color = new Color(0.25f, 0.85f, 1f, 1f);
            const float lineThickness = 2f;
            int samples = Mathf.Clamp(Mathf.CeilToInt(graph.width), 16, 1024);
            float previousY = graph.yMax - curve.Evaluate(0f) * graph.height;
            for (int index = 1; index <= samples; index++)
            {
                float x = index / (float)samples;
                float y = graph.yMax - curve.Evaluate(x) * graph.height;
                float top = Mathf.Max(graph.y, Mathf.Min(previousY, y) - lineThickness * 0.5f);
                float bottom = Mathf.Min(graph.yMax, Mathf.Max(previousY, y) + lineThickness * 0.5f);
                float columnX = graph.x + (index - 1) * graph.width / samples;
                GUI.DrawTexture(
                    new Rect(columnX, top, Mathf.Max(1f, graph.width / samples + 0.5f), Mathf.Max(1f, bottom - top)),
                    Texture2D.whiteTexture);
                previousY = y;
            }

            for (int index = 0; index < curve.PointCount; index++)
            {
                Vector2 point = curve.GetPoint(index);

                // Маркер прижат к полю: у крайних точек половина квадрата
                // иначе вылезала за график.
                Rect pointRect = new(
                    Mathf.Clamp(graph.x + point.x * graph.width - 4f, graph.x, graph.xMax - 8f),
                    Mathf.Clamp(graph.yMax - point.y * graph.height - 4f, graph.y, graph.yMax - 8f),
                    8f,
                    8f);
                GUI.color = ReferenceEquals(_selectedCurve, curve) &&
                    _selectedCurvePoint == index
                    ? Color.yellow
                    : Color.white;
                GUI.DrawTexture(pointRect, Texture2D.whiteTexture);
            }

            GUI.color = previousColor;
        }

        Event current = Event.current;
        if (current.type == EventType.MouseDown && graph.Contains(current.mousePosition))
        {
            int nearest = FindCurvePoint(graph, curve, current.mousePosition);
            if (current.button == 1)
            {
                if (nearest >= 0 && curve.RemovePoint(nearest))
                {
                    _selectedCurve = curve;
                    _selectedCurvePoint = -1;
                    current.Use();
                }
            }
            else if (current.button == 0)
            {
                _selectedCurve = curve;
                _selectedCurvePoint = nearest >= 0
                    ? nearest
                    : curve.AddPoint(GraphToCurve(graph, current.mousePosition));
                _draggingCurvePoint = _selectedCurvePoint >= 0;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                current.Use();
            }
        }
        else if (current.type == EventType.MouseDrag &&
                 _draggingCurvePoint &&
                 ReferenceEquals(_selectedCurve, curve))
        {
            curve.SetPoint(_selectedCurvePoint, GraphToCurve(graph, current.mousePosition));
            current.Use();
        }
        else if (current.type == EventType.MouseUp && _draggingCurvePoint)
        {
            _draggingCurvePoint = false;
            GUIUtility.hotControl = 0;
            current.Use();
        }
    }

    private static int FindCurvePoint(Rect graph, ColorGradeCurve curve, Vector2 mousePosition)
    {
        int nearest = -1;
        float nearestDistance = 12f;
        for (int index = 0; index < curve.PointCount; index++)
        {
            Vector2 point = curve.GetPoint(index);
            Vector2 graphPoint = new(
                graph.x + point.x * graph.width,
                graph.yMax - point.y * graph.height);
            float distance = Vector2.Distance(mousePosition, graphPoint);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static Vector2 GraphToCurve(Rect graph, Vector2 position) => new(
        Mathf.InverseLerp(graph.x, graph.xMax, position.x),
        Mathf.InverseLerp(graph.yMax, graph.y, position.y));

    private static Vector2 CurveToGraph(Rect graph, float value, float x) => new(
        graph.x + x * graph.width,
        graph.yMax - value * graph.height);

    public static string GetLayerTitle(ColorGradeLayer layer) => layer switch
    {
        ColorGradeLayer.Exposure => "Экспозиция",
        ColorGradeLayer.WhiteBalance => "Баланс белого",
        ColorGradeLayer.Cdl => "ASC CDL",
        ColorGradeLayer.Saturation => "Насыщенность",
        ColorGradeLayer.Contrast => "Контраст",
        ColorGradeLayer.Curve => "Кривая вывода",
        _ => layer.ToString(),
    };

    public void DrawActions(GUIStyle sectionStyle, GUIStyle wrappedLabelStyle)
    {
        ToolTheme.Separator();
        GUILayout.Label("ФАЙЛ И ЭКСПОРТ", sectionStyle);
        if (_state.HasPreviewOverrides)
        {
            using (ToolLayout.Vertical(ToolTheme.Card))
            {
                GUILayout.Label(
                    "Соло/обход меняют только предпросмотр. Сохранение, экспорт и " +
                    "зоны содержат полный грейд со всеми слоями.",
                    ToolTheme.WarningLabel);
                if (GUILayout.Button("Показать полный грейд", ToolTheme.ActiveButton))
                {
                    _clearPreviewRequested = true;
                }
            }
        }

        using (ToolLayout.Horizontal())
        {
            GUI.enabled = _state.CanUndo;
            if (GUILayout.Button("Undo", ToolTheme.SecondaryButton))
            {
                _state.Undo();
                _state.CancelHistoryFrame();
            }

            GUI.enabled = _state.CanRedo;
            if (GUILayout.Button("Redo", ToolTheme.SecondaryButton))
            {
                _state.Redo();
                _state.CancelHistoryFrame();
            }

            GUI.enabled = true;
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Сохранить", ToolTheme.ActiveButton))
            {
                SetStatus(
                    ColorGradeFile.Save(_state, _zones),
                    "Сохранено: " + ColorGradeFile.Path,
                    "Ошибка сохранения");
            }

            if (GUILayout.Button("Загрузить", ToolTheme.SecondaryButton))
            {
                _loadRequested = true;
            }

            if (GUILayout.Button("Сбросить всё", ToolTheme.DangerButton))
            {
                _resetAllRequested = true;
            }
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Экспорт .cdl", ToolTheme.SecondaryButton))
            {
                SetStatus(
                    ColorGradeFile.ExportCdl(_state),
                    "ASC CDL: " + ColorGradeFile.CdlPath,
                    "Ошибка экспорта ASC CDL");
            }

            if (GUILayout.Button("Копировать код", ToolTheme.SecondaryButton))
            {
                GUIUtility.systemCopyBuffer = ColorGradeFile.ToLookSource(_state);
                SetStatus(true, "Блок PostProcessLook скопирован", string.Empty);
            }
        }

        GUILayout.Label(
            "ASC CDL содержит только Slope/Offset/Power и насыщенность; " +
            "полный look переносится кнопкой «копировать код».",
            wrappedLabelStyle);

        GUILayout.Label("ПРЕСЕТЫ", sectionStyle);
        _presetName = GUILayout.TextField(_presetName);
        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Сохранить пресет", ToolTheme.ActiveButton))
            {
                SetStatus(
                    ColorGradeFile.SavePreset(_state, _zones, _presetName),
                    $"Пресет сохранён: {_presetName}",
                    $"Ошибка сохранения пресета: {_presetName}");
            }

            if (GUILayout.Button("Загрузить пресет", ToolTheme.SecondaryButton))
            {
                _loadPresetRequested = true;
            }
        }

        string[] presets = ColorGradeFile.ListPresets();
        GUILayout.Label(
            presets.Length == 0
                ? "Сохранённых пресетов пока нет."
                : "Доступно: " + string.Join(", ", presets),
            wrappedLabelStyle);

        GUIStyle statusStyle = _statusIsError ? ToolTheme.ErrorLabel : ToolTheme.SuccessLabel;
        GUILayout.Label(_status ?? string.Empty, statusStyle);
    }

    public void ApplyPendingActions()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_bypassLayerRequested.HasValue)
        {
            _state.SetBypassed(_bypassLayerRequested.Value, _bypassValueRequested);
            _bypassLayerRequested = null;
        }

        if (_soloChangeRequested)
        {
            _state.Solo = _soloRequested;
            _soloChangeRequested = false;
            _soloRequested = null;
        }

        if (_clearPreviewRequested)
        {
            _state.ClearPreviewOverrides();
            _clearPreviewRequested = false;
        }

        if (_clearBypassesRequested)
        {
            for (int i = 0; i < 6; i++)
            {
                _state.SetBypassed((ColorGradeLayer)i, false);
            }

            _clearBypassesRequested = false;
        }

        if (_loadRequested)
        {
            bool loaded = ColorGradeFile.TryLoad(_state, _zones);
            _numberText.Clear();
            SetStatus(
                loaded,
                "Загружено: " + ColorGradeFile.Path,
                "Файл не загружен: " + ColorGradeFile.Path);
            _loadRequested = false;
        }

        if (_loadPresetRequested)
        {
            bool loaded = ColorGradeFile.TryLoadPreset(_state, _zones, _presetName);
            _numberText.Clear();
            SetStatus(
                loaded,
                $"Пресет загружен: {_presetName}",
                $"Пресет не загружен: {_presetName}");
            _loadPresetRequested = false;
        }

        if (_resetAllRequested)
        {
            _state.ResetToLook();
            _zones.Clear();
            _zones.Enabled = false;
            _numberText.Clear();
            SetStatus(true, "Возвращен PostProcessLook; зоны очищены", string.Empty);
            _resetAllRequested = false;
        }
    }

    public float Slider(string id, string label, float value, float minimum, float maximum)
    {
        if (!_numberText.TryGetValue(id, out string? text))
        {
            text = value.ToString("0.###", CultureInfo.InvariantCulture);
            _numberText[id] = text;
        }

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, _SliderLabelWidth);
            float sliderMinimum = minimum;
            float sliderMaximum = maximum;
            if (Event.current.shift)
            {
                float fineRange = (maximum - minimum) * 0.1f;
                sliderMinimum = Mathf.Max(minimum, value - fineRange);
                sliderMaximum = Mathf.Min(maximum, value + fineRange);
            }

            float result = GUILayout.HorizontalSlider(value, sliderMinimum, sliderMaximum);
            if (!Mathf.Approximately(result, value))
            {
                text = result.ToString("0.###", CultureInfo.InvariantCulture);
                _numberText[id] = text;
            }

            if (!_controlNames.TryGetValue(id, out string? controlName))
            {
                controlName = "grade." + id;
                _controlNames[id] = controlName;
            }

            GUI.SetNextControlName(controlName);
            string edited = GUILayout.TextField(text, _SliderFieldWidth);
            if (edited != text)
            {
                edited = edited.Replace(',', '.');
                _numberText[id] = edited;
                if (_invalidNumberId == id)
                {
                    _invalidNumberId = null;
                    _status = null;
                    _statusIsError = false;
                }

                if (float.TryParse(
                        edited,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed) &&
                    !float.IsNaN(parsed) &&
                    !float.IsInfinity(parsed))
                {
                    result = Mathf.Clamp(parsed, minimum, maximum);
                }
            }

            bool focused = FocusedControlName() == controlName;
            if (focused &&
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return ||
                 Event.current.keyCode == KeyCode.KeypadEnter))
            {
                GUI.FocusControl(null);
                focused = false;
                Event.current.Use();
            }

            if (!focused)
            {
                if (float.TryParse(
                        _numberText[id],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float committed) &&
                    !float.IsNaN(committed) &&
                    !float.IsInfinity(committed))
                {
                    // Без фокуса источник истины — значение, а не текст. Раньше
                    // здесь было `result = committed`: слайдер возвращал число из
                    // своей строки и на следующем же событии откатывал всё, что
                    // поменяли в обход него, — колесо коррекции, перетаскивание
                    // точки кривой, пипетку. Набранный текст уже применён в той
                    // ветке, где поле было в фокусе, так что терять нечего.
                    // Порог — точность формата "0.###": без него значение с
                    // четвёртым знаком переформатировалось бы в каждом событии.
                    if (Mathf.Abs(committed - result) > 0.0005f)
                    {
                        _numberText[id] = result.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    _numberText[id] = value.ToString("0.###", CultureInfo.InvariantCulture);
                    _invalidNumberId = id;
                    _statusIsError = true;
                    _status = $"Некорректное число «{label}»; оставлено предыдущее значение.";
                }
            }

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                Event.current.clickCount == 2 &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                result = NeutralValue(id, minimum, maximum);
                _numberText[id] = result.ToString("0.###", CultureInfo.InvariantCulture);
                Event.current.Use();
            }

            return result;
        }
    }

    private static float NeutralValue(string id, float minimum, float maximum)
    {
        float neutral = id switch
        {
            "exposure" or "black-point" or "highlight-recovery" => 0f,
            "input-white-point" or "whitepoint" or "white-point" => 1f,
            "grey-out" => 0.18f,
            "curve-slope" => 1f,
            "gamut-compression" => 1f,
            "toe-stops" => 12f,
            "shoulder-power" => 4f,
            "toe-power" => 1.6f,
            "path-power" => 3f,
            "path-to-white" => 0f,
            "saturation" or "cdl.saturation" => 1f,
            "pivot" => 0.5f,
            "hue" or "temperature" or "tint" => 0f,
            _ when id.EndsWith(".slope.r", StringComparison.Ordinal) ||
                id.EndsWith(".slope.g", StringComparison.Ordinal) ||
                id.EndsWith(".slope.b", StringComparison.Ordinal) ||
                id == "master.slope" => 1f,
            _ when id.EndsWith(".power.r", StringComparison.Ordinal) ||
                id.EndsWith(".power.g", StringComparison.Ordinal) ||
                id.EndsWith(".power.b", StringComparison.Ordinal) ||
                id == "master.power" ||
                id.StartsWith("primary.gamma.", StringComparison.Ordinal) ||
                id == "primary.master.gamma" => 1f,
            _ when id.StartsWith("primary.gain.", StringComparison.Ordinal) ||
                id == "primary.master.gain" => 1f,
            _ when id.Contains("center", StringComparison.OrdinalIgnoreCase) =>
                id.Contains("hue", StringComparison.OrdinalIgnoreCase) ? 120f : 0.5f,
            _ when id.Contains("multiplier", StringComparison.OrdinalIgnoreCase) => 1f,
            _ when id.Contains("shift", StringComparison.OrdinalIgnoreCase) => 0f,
            _ when id.Contains("power", StringComparison.OrdinalIgnoreCase) => 1f,
            _ => 0f,
        };
        return Mathf.Clamp(neutral, minimum, maximum);
    }

    public Vector3 TripletSlider(
        string id,
        string label,
        Vector3 value,
        float minimum,
        float maximum)
    {
        GUILayout.Label(label, ToolTheme.SectionLabel);
        string[] ids = ChannelIds(id);
        return new Vector3(
            Slider(ids[0], "  R", value.x, minimum, maximum),
            Slider(ids[1], "  G", value.y, minimum, maximum),
            Slider(ids[2], "  B", value.z, minimum, maximum));
    }

    private string[] ChannelIds(string id)
    {
        if (!_channelIds.TryGetValue(id, out string[]? ids))
        {
            ids = [id + ".r", id + ".g", id + ".b"];
            _channelIds[id] = ids;
        }

        return ids;
    }

    private static void DrawPrimaryWheel(
        string title,
        ref Vector3 value,
        Vector3 neutral,
        float minimum,
        float maximum,
        string controlID)
    {
        GUILayout.Label(title, ToolTheme.SectionLabel);
        Rect rect = GUILayoutUtility.GetRect(WheelSize, WheelSize, _WheelOptions);

        // Квадрат по меньшей стороне: в узкой колонке GetRect может отдать
        // меньше запрошенного, и растянутое колесо превращалось в эллипс,
        // по которому промахивалась мышь.
        float side = Mathf.Min(rect.width, rect.height);
        rect = new Rect(rect.x + (rect.width - side) * 0.5f, rect.y, side, side);
        float wheelRadius = side * 0.5f;

        // Амплитуда цветового сдвига на краю колеса.
        float amplitude = (maximum - minimum) * 0.25f;

        // Один id на каждом событии: захват мыши по нему живёт и за краем колеса.
        int id = GUIUtility.GetControlID(controlID.GetHashCode(), FocusType.Passive, rect);
        Event current = Event.current;
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (GUI.enabled && current.button == 0 && IsInsideWheel(rect, current.mousePosition))
                {
                    if (current.clickCount == 2)
                    {
                        // Двойной клик возвращает к нейтрали, сохраняя яркость.
                        value = neutral + Vector3.one * ChannelMean(value - neutral);
                    }
                    else
                    {
                        GUIUtility.hotControl = id;
                        value = WheelValue(rect, current.mousePosition, value, neutral, amplitude, minimum, maximum);
                    }

                    GUI.changed = true;
                    current.Use();
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    value = WheelValue(rect, current.mousePosition, value, neutral, amplitude, minimum, maximum);
                    GUI.changed = true;
                    current.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }

                break;

            case EventType.Repaint:
                EnsureWheelTexture();
                if (_wheelTexture != null)
                {
                    // alphaBlend обязателен: углы текстуры прозрачные, и без
                    // смешивания колесо рисовалось чёрным квадратом.
                    GUI.DrawTexture(rect, _wheelTexture, ScaleMode.StretchToFill, true);
                }

                DrawWheelMarker(rect, WheelPosition(value - neutral, amplitude) * wheelRadius, GUIUtility.hotControl == id);
                break;

            default:
                break;
        }

        GUILayout.Label("центр = нейтраль · направление = оттенок · радиус = сила · двойной клик = сброс", ToolTheme.MutedLabel);
    }

    private const float WheelSize = 128f;
    private static readonly GUILayoutOption[] _WheelOptions =
    [
        GUILayout.Width(WheelSize),
        GUILayout.Height(WheelSize),
    ];

    // Направления каналов на колесе: красный 0°, зелёный 120°, синий 240° —
    // те же, что у оттенков HSV в текстуре. Сдвиг по трём косинусам имеет
    // нулевое среднее, то есть меняет цвет и не трогает яркость.
    private static readonly float[] _ChannelCos = [1f, -0.5f, -0.5f];
    private static readonly float[] _ChannelSin = [0f, 0.8660254f, -0.8660254f];

    private static bool IsInsideWheel(Rect rect, Vector2 mouse) =>
        (mouse - rect.center).sqrMagnitude <= rect.width * rect.width * 0.25f;

    private static float ChannelMean(Vector3 value) => (value.x + value.y + value.z) / 3f;

    private static Vector3 WheelValue(
        Rect rect,
        Vector2 mouse,
        Vector3 current,
        Vector3 neutral,
        float amplitude,
        float minimum,
        float maximum)
    {
        // У текстуры ось Y смотрит вверх, у мыши IMGUI — вниз. Без смены знака
        // оттенок под курсором был зеркальным: зелёный давал синий.
        Vector2 centered = (mouse - rect.center) / (rect.width * 0.5f);
        centered.y = -centered.y;
        float radius = Mathf.Clamp01(centered.magnitude);
        float angle = Mathf.Atan2(centered.y, centered.x);
        float cos = Mathf.Cos(angle) * radius * amplitude;
        float sin = Mathf.Sin(angle) * radius * amplitude;

        // Яркость (среднее каналов) сохраняется: колесо двигает только цвет.
        float mean = ChannelMean(current - neutral);
        return new Vector3(
            Mathf.Clamp(neutral.x + mean + cos * _ChannelCos[0] + sin * _ChannelSin[0], minimum, maximum),
            Mathf.Clamp(neutral.y + mean + cos * _ChannelCos[1] + sin * _ChannelSin[1], minimum, maximum),
            Mathf.Clamp(neutral.z + mean + cos * _ChannelCos[2] + sin * _ChannelSin[2], minimum, maximum));
    }

    // Обратное к WheelValue: где на колесе лежит текущее значение (единичный
    // круг, ось Y вниз, как у IMGUI).
    private static Vector2 WheelPosition(Vector3 offset, float amplitude)
    {
        if (amplitude <= 0f)
        {
            return Vector2.zero;
        }

        float x = (offset.x * _ChannelCos[0] + offset.y * _ChannelCos[1] + offset.z * _ChannelCos[2]) * (2f / 3f);
        float y = (offset.x * _ChannelSin[0] + offset.y * _ChannelSin[1] + offset.z * _ChannelSin[2]) * (2f / 3f);
        Vector2 position = new Vector2(x, -y) / amplitude;
        return position.sqrMagnitude > 1f ? position.normalized : position;
    }

    private static void DrawWheelMarker(Rect rect, Vector2 offset, bool active)
    {
        Color previous = GUI.color;
        Vector2 center = rect.center + offset;
        float outer = active ? 10f : 8f;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(center.x - outer * 0.5f, center.y - outer * 0.5f, outer, outer), Texture2D.whiteTexture);
        GUI.color = active ? Color.yellow : Color.white;
        float inner = outer - 4f;
        GUI.DrawTexture(new Rect(center.x - inner * 0.5f, center.y - inner * 0.5f, inner, inner), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void EnsureWheelTexture()
    {
        if (_wheelTexture != null)
        {
            return;
        }

        const int size = 128;
        _wheelTexture = Fodinae.RuntimeTextureFactory.CreateRGBA32NoMip(
            size,
            size,
            "Fodinae.PrimaryColorWheel",
            Fodinae.RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 centered = new(
                    (x + 0.5f) / size * 2f - 1f,
                    (y + 0.5f) / size * 2f - 1f);
                float radius = centered.magnitude;
                if (radius > 1f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float hue = Mathf.Repeat(Mathf.Atan2(centered.y, centered.x) / (Mathf.PI * 2f), 1f);
                Color color = Color.HSVToRGB(hue, radius, 1f);
                color.a = 1f;
                pixels[y * size + x] = color;
            }
        }

        _wheelTexture.SetPixels(pixels);
        _wheelTexture.Apply(false, true);
    }

    private static void ReleaseWheelTexture()
    {
        if (_wheelTexture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_wheelTexture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_wheelTexture);
        }

        _wheelTexture = null;
    }
}
