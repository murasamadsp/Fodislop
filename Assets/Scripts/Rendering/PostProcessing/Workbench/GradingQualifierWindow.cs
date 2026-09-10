#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

internal sealed class GradingQualifierWindow : ToolWindow
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeQualifier _qualifier;
    private string _lutPath = string.Empty;
    private Vector2 _scroll;
    private readonly Dictionary<string, string> _numberText = [];

    public GradingQualifierWindow(ColorGradeState state)
        : base("Qualifier / Secondary", new Rect(16f, 16f, 410f, 660f))
    {
        _state = state;
        _qualifier = state.Qualifier;
        Visible = false;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(360f, 420f);

    protected override void OnPlaySessionReset()
    {
        ColorGradeScreenSampler.Cancel();
        _scroll = default;
        _lutPath = _state.LutPath;
        _numberText.Clear();
    }

    protected override void OnDispose()
    {
        ColorGradeScreenSampler.Cancel();
    }

    protected override void DrawContent()
    {
        // This window edits the same authored look as the layer window. Keep
        // qualifier and LUT edits undoable even when the layer window is hidden.
        _state.BeginHistoryFrame();

        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            _qualifier.Enabled = GUILayout.Toggle(
                _qualifier.Enabled,
                "●  Включить qualifier",
                SegmentedButtonStyle);
            _qualifier.Invert = GUILayout.Toggle(
                _qualifier.Invert,
                "Invert matte",
                ToolTheme.SegmentedButton);

            GUILayout.Label("HUE RANGE", SectionLabelStyle);
            _qualifier.HueCenter = Slider("hue center", _qualifier.HueCenter, 0f, 360f);
            _qualifier.HueWidth = Slider("hue width", _qualifier.HueWidth, 0f, 180f);
            _qualifier.HueSoftness = Slider("hue softness", _qualifier.HueSoftness, 0f, 180f);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Eyedropper sample", ToolTheme.SecondaryButton))
                {
                    ColorGradeScreenSampler.Arm(sample =>
                    {
                        Color.RGBToHSV(sample, out float hue, out float saturation, out _);
                        _qualifier.HueCenter = hue * 360f;
                        _qualifier.SaturationCenter = saturation;
                        _qualifier.LuminanceCenter = Mathf.Clamp01(
                            sample.r * 0.2126f +
                            sample.g * 0.7152f +
                            sample.b * 0.0722f);
                        _numberText.Remove("hue center");
                        _numberText.Remove("sat center");
                        _numberText.Remove("luma center");
                    });
                }

                if (ColorGradeScreenSampler.IsArmed &&
                    GUILayout.Button("Cancel", ToolTheme.DangerButton))
                {
                    ColorGradeScreenSampler.Cancel();
                }

                if (GUILayout.Button("Add sample", ToolTheme.SecondaryButton))
                {
                    _qualifier.AddHueSample(_qualifier.HueCenter);
                }

                if (GUILayout.Button("Remove sample", ToolTheme.DangerButton))
                {
                    _qualifier.RemoveLastHueSample();
                }
            }

            GUILayout.Label(
                $"Hue samples: {_qualifier.HueSamples.Count}/{ColorGradeQualifier.MaxHueSamples}",
                ToolTheme.MutedLabel);

            GUILayout.Label("SATURATION RANGE", SectionLabelStyle);
            _qualifier.SaturationCenter = Slider("sat center", _qualifier.SaturationCenter, 0f, 1f);
            _qualifier.SaturationWidth = Slider("sat width", _qualifier.SaturationWidth, 0f, 1f);
            _qualifier.SaturationSoftness = Slider("sat softness", _qualifier.SaturationSoftness, 0f, 1f);

            GUILayout.Label("LUMINANCE RANGE", SectionLabelStyle);
            _qualifier.LuminanceCenter = Slider("luma center", _qualifier.LuminanceCenter, 0f, 1f);
            _qualifier.LuminanceWidth = Slider("luma width", _qualifier.LuminanceWidth, 0f, 1f);
            _qualifier.LuminanceSoftness = Slider("luma softness", _qualifier.LuminanceSoftness, 0f, 1f);

            GUILayout.Label("LOCAL CORRECTION", SectionLabelStyle);
            _qualifier.HueShift = Slider("hue shift", _qualifier.HueShift, -180f, 180f);
            _qualifier.Saturation = Slider("saturation", _qualifier.Saturation, 0f, 2f);
            _qualifier.Exposure = Slider("exposure EV", _qualifier.Exposure, -8f, 8f);
            _qualifier.Temperature = Slider("temperature", _qualifier.Temperature, -100f, 100f);
            _qualifier.Tint = Slider("tint", _qualifier.Tint, -100f, 100f);
            _qualifier.Lift = Triplet("lift", _qualifier.Lift, -0.5f, 0.5f);
            _qualifier.Gamma = Triplet("gamma", _qualifier.Gamma, 0.1f, 4f);
            _qualifier.Gain = Triplet("gain", _qualifier.Gain, 0f, 4f);

            if (GUILayout.Button("Reset qualifier", ToolTheme.DangerButton))
            {
                _qualifier.Reset();
                _numberText.Clear();
            }

            GUILayout.Label("LUT", SectionLabelStyle);
            _lutPath = GUILayout.TextField(_lutPath);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load .cube", ToolTheme.SecondaryButton))
                {
                    if (!_state.LoadLut(_lutPath, out string error))
                    {
                        Debug.LogWarning($"[ColorGrade] LUT не загружен: {error}");
                    }
                    else
                    {
                        _numberText.Remove("LUT intensity");
                    }
                }

                if (GUILayout.Button("Clear LUT", ToolTheme.DangerButton))
                {
                    _state.ClearLut();
                    _lutPath = string.Empty;
                    _numberText.Remove("LUT intensity");
                }
            }

            _state.LutIntensity = Slider("LUT intensity", _state.LutIntensity, 0f, 1f);
            GUILayout.Label(
                _state.Lut == null
                    ? "LUT не загружен."
                    : $"{_state.Lut.Type}, size {_state.Lut.Size}, {_state.Lut.Path}",
                ToolTheme.MutedLabel);
            GUILayout.Label("LUT input color space", ToolTheme.FieldLabel);
            bool srgb = GUILayout.Toggle(
                _state.LutColorSpace == ColorGradeLutColorSpace.SrgbRec709,
                "sRGB Rec.709 (off = Linear Rec.709)",
                ToolTheme.SegmentedButton);
            _state.LutColorSpace = srgb
                ? ColorGradeLutColorSpace.SrgbRec709
                : ColorGradeLutColorSpace.LinearRec709;

            GUILayout.Label("COLOR MANAGEMENT", SectionLabelStyle);
            ColorGradeColorManagement management = _state.ColorManagement;
            management.InputColorSpace = EnumCycle(
                "Input", management.InputColorSpace, "Rec.709", "Display P3", "Rec.2020");
            management.WorkingColorSpace = EnumCycle(
                "Working", management.WorkingColorSpace, "Rec.709", "Display P3", "Rec.2020");
            management.OutputColorSpace = EnumCycle(
                "Output", management.OutputColorSpace, "Rec.709", "Display P3", "Rec.2020");
            management.InputTransfer = EnumCycle(
                "Input transfer", management.InputTransfer, "Linear", "sRGB", "PQ", "HLG");
            management.OutputTransfer = EnumCycle(
                "Output transfer", management.OutputTransfer, "Linear", "sRGB", "PQ", "HLG");
            management.DynamicRange = EnumCycle(
                "Dynamic range", management.DynamicRange, "SDR", "HDR");
            GUILayout.Label(
                "Output transfer — контракт display pipeline; финальное кодирование " +
                "sRGB/PQ/HLG выполняет URP один раз.",
                ToolTheme.MutedLabel);
            management.ReferenceMode = EnumCycle(
                "Reference", management.ReferenceMode, "Scene-referred", "Display-referred");

            GUILayout.Label(
                "Маска вычисляется в grading space; hue range корректно " +
                "пересекает 0°/360°. Multi-sample можно расширить кнопкой Add sample.",
                ToolTheme.MutedLabel);
        }

        _state.CommitHistoryFrame();
    }

    private float Slider(string label, float value, float min, float max)
    {
        if (!_numberText.TryGetValue(label, out string? text))
        {
            text = value.ToString("0.###", CultureInfo.InvariantCulture);
            _numberText[label] = text;
        }

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, GUILayout.Width(120f));
            float sliderMin = min;
            float sliderMax = max;
            if (Event.current.shift)
            {
                float fineRange = (max - min) * 0.1f;
                sliderMin = Mathf.Max(min, value - fineRange);
                sliderMax = Mathf.Min(max, value + fineRange);
            }

            float result = GUILayout.HorizontalSlider(value, sliderMin, sliderMax);
            if (!Mathf.Approximately(result, value))
            {
                text = result.ToString("0.###", CultureInfo.InvariantCulture);
                _numberText[label] = text;
            }

            string edited = GUILayout.TextField(text, GUILayout.Width(64f));
            if (edited != text)
            {
                edited = edited.Replace(',', '.');
                _numberText[label] = edited;
                if (float.TryParse(
                        edited,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed) &&
                    float.IsFinite(parsed))
                {
                    result = Mathf.Clamp(parsed, min, max);
                }
            }

            if (!GUI.changed &&
                float.TryParse(
                    _numberText[label],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float committed) &&
                float.IsFinite(committed))
            {
                result = Mathf.Clamp(committed, min, max);
            }

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                Event.current.clickCount == 2 &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                result = NeutralValue(label, min, max);
                _numberText[label] = result.ToString("0.###", CultureInfo.InvariantCulture);
                Event.current.Use();
            }

            return result;
        }
    }

    private static float NeutralValue(string label, float min, float max)
    {
        float neutral = label switch
        {
            "hue center" => 120f,
            "hue width" => 30f,
            "hue softness" => 15f,
            "sat center" or "sat width" => 0.5f,
            "sat softness" => 0.1f,
            "luma center" or "luma width" => 0.5f,
            "luma softness" => 0.1f,
            "saturation" => 1f,
            "LUT intensity" => 0f,
            _ when label.Contains("gamma", StringComparison.OrdinalIgnoreCase) => 1f,
            _ when label.Contains("gain", StringComparison.OrdinalIgnoreCase) => 1f,
            _ => 0f,
        };

        return Mathf.Clamp(neutral, min, max);
    }

    private Vector3 Triplet(string label, Vector3 value, float min, float max)
    {
        GUILayout.Label(label, ToolTheme.FieldLabel);
        return new Vector3(
            Slider(label + " R", value.x, min, max),
            Slider(label + " G", value.y, min, max),
            Slider(label + " B", value.z, min, max));
    }

    private static T EnumCycle<T>(string label, T value, params string[] names)
        where T : struct, Enum
    {
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(label, ToolTheme.FieldLabel, GUILayout.Width(120f));
            int index = Mathf.Clamp(Convert.ToInt32(value), 0, names.Length - 1);
            if (GUILayout.Button(names[index], ToolTheme.SegmentedButton))
            {
                value = (T)(object)((index + 1) % names.Length);
            }

            return value;
        }
    }
}
