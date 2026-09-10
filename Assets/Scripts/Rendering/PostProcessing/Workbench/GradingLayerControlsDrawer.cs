#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

/// <summary>
/// Отрисовка ползунков, числовых полей ввода и файловых действий для слоёв цветового конвейера.
/// </summary>
internal sealed class GradingLayerControlsDrawer
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private readonly Dictionary<string, string> _numberText = [];
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
        _state.BlackPoint = Slider(
            "black-point", "black point", _state.BlackPoint,
            ColorGradeState.BlackPointMin, ColorGradeState.BlackPointMax);
        _state.InputWhitePoint = Slider(
            "input-white-point", "white point", _state.InputWhitePoint,
            ColorGradeState.InputWhitePointMin, ColorGradeState.InputWhitePointMax);
        _state.HighlightRecovery = Slider(
            "highlight-recovery", "recovery", _state.HighlightRecovery,
            ColorGradeState.HighlightRecoveryMin, ColorGradeState.HighlightRecoveryMax);
        GUILayout.Label(
            "Входной диапазон HDR. Нейтрально: black 0, white 1, recovery 0.",
            ToolTheme.MutedLabel);
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

        GUILayout.Label("HUE VS SATURATION", ToolTheme.SectionLabel);
        if (GUILayout.Button("Eyedropper: Hue vs Saturation", ToolTheme.SecondaryButton))
        {
            ColorGradeScreenSampler.Arm(sample =>
            {
                Vector4 current = _state.HueVsSaturation;
                current.x = HueFromRGB(sample);
                _state.HueVsSaturation = current;
                _numberText.Remove("hue-vs-sat.center");
            });
        }

        Vector4 selective = _state.HueVsSaturation;
        selective.x = Slider("hue-vs-sat.center", "  центр °", selective.x, 0f, 360f);
        selective.y = Slider("hue-vs-sat.width", "  ширина °", selective.y, 0f, 180f);
        selective.z = Slider("hue-vs-sat.feather", "  feather °", selective.z, 0f, 180f);
        selective.w = Slider("hue-vs-sat.multiplier", "  saturation ×", selective.w, 0f, 2f);
        _state.HueVsSaturation = selective;
        GUILayout.Label("HUE VS HUE / LUMINANCE", ToolTheme.SectionLabel);
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Eyedropper: Hue vs Hue", ToolTheme.SecondaryButton))
            {
                ColorGradeScreenSampler.Arm(sample =>
                {
                    Vector4 current = _state.HueVsHue;
                    current.x = HueFromRGB(sample);
                    _state.HueVsHue = current;
                    _numberText.Remove("hue-vs-hue.center");
                });
            }

            if (GUILayout.Button("Eyedropper: Hue vs Luma", ToolTheme.SecondaryButton))
            {
                ColorGradeScreenSampler.Arm(sample =>
                {
                    Vector4 current = _state.HueVsLuminance;
                    current.x = HueFromRGB(sample);
                    _state.HueVsLuminance = current;
                    _numberText.Remove("hue-vs-luma.center");
                });
            }
        }
        Vector4 hueShift = _state.HueVsHue;
        hueShift.x = Slider("hue-vs-hue.center", "  hue center °", hueShift.x, 0f, 360f);
        hueShift.y = Slider("hue-vs-hue.width", "  hue width °", hueShift.y, 0f, 180f);
        hueShift.z = Slider("hue-vs-hue.feather", "  hue feather °", hueShift.z, 0f, 180f);
        hueShift.w = Slider("hue-vs-hue.shift", "  hue shift °", hueShift.w, -180f, 180f);
        _state.HueVsHue = hueShift;
        Vector4 hueLuma = _state.HueVsLuminance;
        hueLuma.x = Slider("hue-vs-luma.center", "  luma center °", hueLuma.x, 0f, 360f);
        hueLuma.y = Slider("hue-vs-luma.width", "  luma width °", hueLuma.y, 0f, 180f);
        hueLuma.z = Slider("hue-vs-luma.feather", "  luma feather °", hueLuma.z, 0f, 180f);
        hueLuma.w = Slider("hue-vs-luma.amount", "  luma amount", hueLuma.w, -1f, 1f);
        _state.HueVsLuminance = hueLuma;
        Vector4 lumaSat = _state.LuminanceVsSaturation;
        lumaSat.x = Slider("luma-vs-sat.center", "  luma center", lumaSat.x, 0f, 1f);
        lumaSat.y = Slider("luma-vs-sat.width", "  luma width", lumaSat.y, 0f, 1f);
        lumaSat.z = Slider("luma-vs-sat.feather", "  luma feather", lumaSat.z, 0f, 1f);
        lumaSat.w = Slider("luma-vs-sat.multiplier", "  luma sat ×", lumaSat.w, 0f, 2f);
        _state.LuminanceVsSaturation = lumaSat;
        Vector4 satSat = _state.SaturationVsSaturation;
        satSat.x = Slider("sat-vs-sat.center", "  sat center", satSat.x, 0f, 1f);
        satSat.y = Slider("sat-vs-sat.width", "  sat width", satSat.y, 0f, 1f);
        satSat.z = Slider("sat-vs-sat.feather", "  sat feather", satSat.z, 0f, 1f);
        satSat.w = Slider("sat-vs-sat.multiplier", "  sat sat ×", satSat.w, 0f, 2f);
        _state.SaturationVsSaturation = satSat;

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
            "Круговой диапазон корректно пересекает 0°/360°. Multi-sample " +
            "объединяет несколько оттенков в одну мягкую маску.",
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
        Rect graph = GUILayoutUtility.GetRect(300f, 150f, GUILayout.ExpandWidth(true));
        DrawCurveGraph(graph, curve);

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ точка", ToolTheme.SecondaryButton, GUILayout.Width(74f)))
            {
                _selectedCurve = curve;
                _selectedCurvePoint = curve.AddPoint(new Vector2(0.5f, 0.5f));
            }

            if (GUILayout.Button("reset", ToolTheme.SecondaryButton, GUILayout.Width(58f)))
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

        if (ReferenceEquals(_selectedCurve, curve) &&
            _selectedCurvePoint >= 0 &&
            _selectedCurvePoint < curve.PointCount)
        {
            Vector2 point = curve.GetPoint(_selectedCurvePoint);
            point.x = Slider(
                id + ".point.x", "  X", point.x, 0f, 1f);
            point.y = Slider(
                id + ".point.y", "  Y", point.y, 0f, 1f);
            curve.SetPoint(_selectedCurvePoint, point);
        }
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

            GUI.color = Color.white;
            Vector2 previous = CurveToGraph(graph, curve.Evaluate(0f), 0f);
            for (int index = 1; index <= 64; index++)
            {
                float x = index / 64f;
                Vector2 samplePoint = CurveToGraph(graph, curve.Evaluate(x), x);
                DrawGraphLine(previous, samplePoint, 2f, new Color(0.25f, 0.85f, 1f, 1f));
                previous = samplePoint;
            }

            for (int index = 0; index < curve.PointCount; index++)
            {
                Vector2 point = curve.GetPoint(index);
                Rect pointRect = new(
                    graph.x + point.x * graph.width - 4f,
                    graph.yMax - point.y * graph.height - 4f,
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

    private static void DrawGraphLine(Vector2 start, Vector2 end, float thickness, Color color)
    {
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        float length = Vector2.Distance(start, end);
        float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.DrawTexture(
            new Rect(start.x, start.y - thickness * 0.5f, length, thickness),
            Texture2D.whiteTexture);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }

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
            using (new GUILayout.VerticalScope(ToolTheme.Card))
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

        using (new GUILayout.HorizontalScope())
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

        using (new GUILayout.HorizontalScope())
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

        using (new GUILayout.HorizontalScope())
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
        using (new GUILayout.HorizontalScope())
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

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, GUILayout.Width(122f));
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

            string controlName = "grade." + id;
            GUI.SetNextControlName(controlName);
            string edited = GUILayout.TextField(text, GUILayout.Width(64f));
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

            bool focused = GUI.GetNameOfFocusedControl() == controlName;
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
                    result = Mathf.Clamp(committed, minimum, maximum);
                    _numberText[id] = result.ToString("0.###", CultureInfo.InvariantCulture);
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
        return new Vector3(
            Slider(id + ".r", "  R", value.x, minimum, maximum),
            Slider(id + ".g", "  G", value.y, minimum, maximum),
            Slider(id + ".b", "  B", value.z, minimum, maximum));
    }

    private static void DrawPrimaryWheel(
        string title,
        ref Vector3 value,
        Vector3 neutral,
        float minimum,
        float maximum,
        string controlId)
    {
        GUILayout.Label(title, ToolTheme.SectionLabel);
        Rect rect = GUILayoutUtility.GetRect(128f, 128f, GUILayout.ExpandWidth(false));
        EnsureWheelTexture();
        if (_wheelTexture != null)
        {
            GUI.DrawTexture(rect, _wheelTexture, ScaleMode.StretchToFill, false);
        }

        Event current = Event.current;
        if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) &&
            current.button == 0 &&
            rect.Contains(current.mousePosition))
        {
            Vector2 centered = (current.mousePosition - rect.center) / (rect.width * 0.5f);
            float radius = Mathf.Clamp01(centered.magnitude);
            if (radius > 0.001f)
            {
                float hue = Mathf.Repeat(Mathf.Atan2(centered.y, centered.x) / (Mathf.PI * 2f), 1f);
                Color color = Color.HSVToRGB(hue, radius, 1f);
                Vector3 offset = new Vector3(color.r, color.g, color.b) - Vector3.one * 0.5f;
                value = new Vector3(
                    Mathf.Clamp(neutral.x + offset.x * (maximum - minimum), minimum, maximum),
                    Mathf.Clamp(neutral.y + offset.y * (maximum - minimum), minimum, maximum),
                    Mathf.Clamp(neutral.z + offset.z * (maximum - minimum), minimum, maximum));
            }

            GUIUtility.hotControl = controlId.GetHashCode();
            current.Use();
        }
        else if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId.GetHashCode())
        {
            GUIUtility.hotControl = 0;
            current.Use();
        }

        GUILayout.Label("центр = neutral · направление = hue · радиус = strength", ToolTheme.MutedLabel);
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

    private static float HueFromRGB(Color color)
    {
        Color.RGBToHSV(color, out float hue, out _, out _);
        return hue * 360f;
    }
}
