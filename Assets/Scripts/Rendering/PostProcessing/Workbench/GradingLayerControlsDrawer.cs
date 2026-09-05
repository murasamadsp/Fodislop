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
    private bool _resetAllRequested;

    public GradingLayerControlsDrawer(ColorGradeState state, ColorGradeZones zones)
    {
        _state = state;
        _zones = zones;
    }

    public string? Status => _status;

    public bool StatusIsError => _statusIsError;

    public void ResetState()
    {
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
        _resetAllRequested = false;
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
        _state.Temperature = Slider(
            "temperature", "температура", _state.Temperature,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);
        _state.Tint = Slider(
            "tint", "оттенок", _state.Tint,
            ColorGradeState.TemperatureMin, ColorGradeState.TemperatureMax);
    }

    private void DrawCdlControls()
    {
        _state.Slope = TripletSlider(
            "slope", "Slope (усиление)", _state.Slope,
            ColorGradeState.SlopeMin, ColorGradeState.SlopeMax);
        _state.Offset = TripletSlider(
            "offset", "Offset (подъём)", _state.Offset,
            ColorGradeState.OffsetMin, ColorGradeState.OffsetMax);
        _state.Power = TripletSlider(
            "power", "Power (гамма)", _state.Power,
            ColorGradeState.PowerMin, ColorGradeState.PowerMax);
    }

    private void DrawSaturationControls()
    {
        _state.Saturation = Slider(
            "saturation", "насыщенность", _state.Saturation,
            ColorGradeState.SaturationMin, ColorGradeState.SaturationMax);
    }

    private void DrawContrastControls()
    {
        _state.Contrast = Slider(
            "contrast", "контраст", _state.Contrast,
            ColorGradeState.ContrastMin, ColorGradeState.ContrastMax);
    }

    private void DrawCurveControls()
    {
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Toggle(
                    _state.Transform == DisplayTransform.Fodinae,
                    "кривая",
                    ToolTheme.SegmentedButton))
            {
                _state.Transform = DisplayTransform.Fodinae;
            }

            if (GUILayout.Toggle(
                    _state.Transform == DisplayTransform.None,
                    "без кривой",
                    ToolTheme.SegmentedButton))
            {
                _state.Transform = DisplayTransform.None;
            }
        }

        _state.WhitePoint = Slider(
            "whitePoint", "белая точка", _state.WhitePoint,
            ColorGradeState.WhitePointMin, ColorGradeState.WhitePointMax);
        _state.GreyOut = Slider(
            "greyOut", "серое на выходе", _state.GreyOut,
            ColorGradeState.GreyOutMin, ColorGradeState.GreyOutMax);
        _state.CurveSlope = Slider(
            "curveSlope", "наклон у серого", _state.CurveSlope,
            ColorGradeState.CurveSlopeMin, ColorGradeState.CurveSlopeMax);
        _state.ShoulderPower = Slider(
            "shoulderPower", "резкость плеча", _state.ShoulderPower,
            ColorGradeState.CurvePowerMin, ColorGradeState.CurvePowerMax);
        _state.ToePower = Slider(
            "toePower", "резкость носка", _state.ToePower,
            ColorGradeState.CurvePowerMin, ColorGradeState.CurvePowerMax);
        _state.ToeStops = Slider(
            "toeStops", "стопов под тени", _state.ToeStops,
            ColorGradeState.ToeStopsMin, ColorGradeState.ToeStopsMax);
        _state.PathToWhiteAmount = Slider(
            "pathToWhiteAmount", "уход в белое", _state.PathToWhiteAmount,
            ColorGradeState.PathToWhiteAmountMin, ColorGradeState.PathToWhiteAmountMax);
        _state.PathToWhitePower = Slider(
            "pathToWhitePower", "степень ухода", _state.PathToWhitePower,
            ColorGradeState.PathToWhitePowerMin, ColorGradeState.PathToWhitePowerMax);

        if (_state.ShoulderPower < 3f)
        {
            GUILayout.Label(
                "Плечо ниже 3: света долго сходятся к белому, и кадр будет молочным.",
                ToolTheme.WarningLabel);
        }
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
            float result = GUILayout.HorizontalSlider(value, minimum, maximum);
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

            return result;
        }
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
}
