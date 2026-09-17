#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradeFile
{
    // 21: кривые «X против Y» хранят сдвиг/множитель вокруг нейтрали 0.5, а не
    // абсолютное значение. Старые кривые в новый смысл честно не переводятся.
    // 22: сжатие гамута на выводе (вкл/выкл и сила).
    // 23: path-to-white удалён из шейдера — поля больше не пишутся; старые
    // файлы читаются (лишние поля JsonUtility игнорирует).
    private const int CurrentVersion = 23;
    private const string FileName = "color_grade.json";

    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static string CdlPath =>
        System.IO.Path.Combine(Application.persistentDataPath, "color_grade.cdl");

    public static string PresetDirectory =>
        System.IO.Path.Combine(Application.persistentDataPath, "color_grade_presets");

    public static string GetPresetPath(string name) =>
        System.IO.Path.Combine(PresetDirectory, SanitizePresetName(name) + ".json");

    public static string[] ListPresets()
    {
        if (!Directory.Exists(PresetDirectory))
        {
            return Array.Empty<string>();
        }

        string[] paths = Directory.GetFiles(PresetDirectory, "*.json");
        for (int index = 0; index < paths.Length; index++)
        {
            paths[index] = System.IO.Path.GetFileNameWithoutExtension(paths[index]);
        }

        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    public static bool SavePreset(
        ColorGradeState state,
        ColorGradeZones? zones,
        string name)
    {
        string presetPath = GetPresetPath(name);
        if (!Save(state, zones))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(PresetDirectory);
            WriteAtomically(presetPath, File.ReadAllText(Path));
            Debug.Log($"[ColorGrade] Пресет сохранён -> {presetPath}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось сохранить пресет {presetPath}: {exception.Message}");
            return false;
        }
    }

    public static bool TryLoadPreset(
        ColorGradeState state,
        ColorGradeZones? zones,
        string name)
    {
        string presetPath = GetPresetPath(name);
        if (!File.Exists(presetPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(presetPath);
            Payload? payload = JsonUtility.FromJson<Payload>(json);
            if (payload == null || payload.Version < 1)
            {
                Debug.LogWarning($"[ColorGrade] Пресет {presetPath} не разобран.");
                return false;
            }

            WriteAtomically(Path, json);
            return TryLoad(state, zones);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось загрузить пресет {presetPath}: {exception.Message}");
            return false;
        }
    }

    [Serializable]
    private sealed class CurvePayload
    {
        public int Interpolation = (int)ColorCurveInterpolation.Smooth;
        public Vector2[] Points = Array.Empty<Vector2>();
    }

    [Serializable]
    private sealed class QualifierPayload
    {
        public bool Enabled;
        public bool Invert;
        public float HueCenter = 120f;
        public float HueWidth = 30f;
        public float HueSoftness = 15f;
        public float SaturationCenter = 0.5f;
        public float SaturationWidth = 0.5f;
        public float SaturationSoftness = 0.1f;
        public float LuminanceCenter = 0.5f;
        public float LuminanceWidth = 0.5f;
        public float LuminanceSoftness = 0.1f;
        public float HueShift;
        public float Saturation = 1f;
        public float Exposure;
        public float Temperature;
        public float Tint;
        public Vector3 Lift;
        public Vector3 Gamma = Vector3.one;
        public Vector3 Gain = Vector3.one;
        public float[] HueSamples = Array.Empty<float>();
    }

    [Serializable]
    private sealed class Payload
    {
        public int Version;
        public int Transform;
        public float Exposure;
        public float Contrast;
        public float Pivot = 0.5f;
        public float Shadows;
        public float Highlights;
        public float Blacks;
        public float Whites;
        public float Toe;
        public float Shoulder;
        public float Saturation;
        public float CdlSaturation = 1f;
        public float Vibrance;
        public float Hue;
        public float Temperature;
        public float Tint;
        public Vector3 Slope;
        public Vector3 Offset;
        public Vector3 Power;
        public Vector3 PrimaryLift;
        public Vector3 PrimaryGamma = Vector3.one;
        public Vector3 PrimaryGain = Vector3.one;
        public Vector3 PrimaryOffset;
        public Vector4 PrimaryMaster = new(0f, 1f, 1f, 0f);
        public Vector3 CdlMaster = new(1f, 0f, 1f);
        public float WhitePoint;
        public float GreyOut;
        public float ShoulderPower;
        public float ToePower;
        public float ToeStops;
        public bool GamutCompressionEnabled = PostProcessLook.Grade.GamutCompressionEnabled;
        public float GamutCompressionStrength = PostProcessLook.Grade.GamutCompressionStrength;
        public CurvePayload MasterCurve = new();
        public CurvePayload RedCurve = new();
        public CurvePayload GreenCurve = new();
        public CurvePayload BlueCurve = new();
        public CurvePayload HueVsHueCurve = new();
        public CurvePayload HueVsSaturationCurve = new();
        public CurvePayload HueVsLuminanceCurve = new();
        public CurvePayload LuminanceVsSaturationCurve = new();
        public CurvePayload SaturationVsSaturationCurve = new();
        public QualifierPayload Qualifier = new();
        public string LutPath = string.Empty;
        public float LutIntensity;
        public int LutColorSpace;
        public int EnabledMask = (1 << 6) - 1;
        // Поля оставлены для чтения файлов v1. Начиная с v2 диагностические
        // состояния предпросмотра не сохраняются вместе с авторским look.
        public int BypassMask;
        public int SoloLayer = -1;
        public bool ZonesEnabled;
        public ColorGradeZonePayload[] Zones = Array.Empty<ColorGradeZonePayload>();
    }

    public static bool Save(ColorGradeState state, ColorGradeZones? zones = null)
    {
        state.Sanitize();
        var payload = new Payload
        {
            Version = CurrentVersion,
            Transform = (int)state.Transform,
            Exposure = state.Exposure,
            Contrast = state.Contrast,
            Pivot = state.Pivot,
            Shadows = state.Shadows,
            Highlights = state.Highlights,
            Blacks = state.Blacks,
            Whites = state.Whites,
            Toe = state.Toe,
            Shoulder = state.Shoulder,
            Saturation = state.Saturation,
            CdlSaturation = state.CdlSaturation,
            Vibrance = state.Vibrance,
            Hue = state.Hue,
            Temperature = state.Temperature,
            Tint = state.Tint,
            Slope = state.Slope,
            Offset = state.Offset,
            Power = state.Power,
            PrimaryLift = state.PrimaryLift,
            PrimaryGamma = state.PrimaryGamma,
            PrimaryGain = state.PrimaryGain,
            PrimaryOffset = state.PrimaryOffset,
            PrimaryMaster = state.PrimaryMaster,
            CdlMaster = state.CdlMaster,
            WhitePoint = state.WhitePoint,
            GreyOut = state.GreyOut,
            ShoulderPower = state.ShoulderPower,
            ToePower = state.ToePower,
            ToeStops = state.ToeStops,
            GamutCompressionEnabled = state.GamutCompressionEnabled,
            GamutCompressionStrength = state.GamutCompressionStrength,
            MasterCurve = ToCurvePayload(state.MasterCurve),
            RedCurve = ToCurvePayload(state.RedCurve),
            GreenCurve = ToCurvePayload(state.GreenCurve),
            BlueCurve = ToCurvePayload(state.BlueCurve),
            HueVsHueCurve = ToCurvePayload(state.HueVsHueCurve),
            HueVsSaturationCurve = ToCurvePayload(state.HueVsSaturationCurve),
            HueVsLuminanceCurve = ToCurvePayload(state.HueVsLuminanceCurve),
            LuminanceVsSaturationCurve = ToCurvePayload(state.LuminanceVsSaturationCurve),
            SaturationVsSaturationCurve = ToCurvePayload(state.SaturationVsSaturationCurve),
            Qualifier = ToQualifierPayload(state.Qualifier),
            LutPath = state.LutPath,
            LutIntensity = state.LutIntensity,
            LutColorSpace = (int)state.LutColorSpace,
            EnabledMask = state.EnabledMask,
            BypassMask = 0,
            SoloLayer = -1,
            ZonesEnabled = zones?.Enabled ?? false,
            Zones = ColorGradeZonePayloads.From(zones),
        };

        try
        {
            WriteAtomically(Path, JsonUtility.ToJson(payload, prettyPrint: true));
            Debug.Log($"[ColorGrade] Сохранено -> {Path}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось сохранить {Path}: {exception.Message}");
            return false;
        }
    }

    public static bool TryLoad(ColorGradeState state, ColorGradeZones? zones = null)
    {
        if (!File.Exists(Path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(Path);
            Payload? payload = JsonUtility.FromJson<Payload>(json);
            // Единственная проверка целостности: файл объявляет себя грейдом.
            // Версионных гейтов по полям больше нет: запись атомарна, рваных
            // файлов не бывает, отсутствующие поля JsonUtility зануляет, а
            // state.Sanitize() приводит их к границам. Новых гейтов под новые
            // поля не заводить — никогда.
            if (payload == null || payload.Version < 1)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} не разобран; грейд оставлен как есть.");
                return false;
            }

            state.Transform = (DisplayTransform)payload.Transform;
            state.Exposure = payload.Exposure;
            state.Contrast = payload.Contrast;
            state.Pivot = payload.Pivot;
            state.Shadows = payload.Shadows;
            state.Highlights = payload.Highlights;
            state.Blacks = payload.Blacks;
            state.Whites = payload.Whites;
            state.Toe = payload.Toe;
            state.Shoulder = payload.Shoulder;
            state.Saturation = payload.Saturation;
            state.CdlSaturation = payload.CdlSaturation;
            state.Vibrance = payload.Vibrance;
            state.Hue = payload.Hue;
            state.Temperature = payload.Temperature;
            state.Tint = payload.Tint;
            state.Slope = payload.Slope;
            state.Offset = payload.Offset;
            state.Power = payload.Power;
            state.PrimaryLift = payload.PrimaryLift;
            state.PrimaryGamma = payload.PrimaryGamma;
            state.PrimaryGain = payload.PrimaryGain;
            state.PrimaryOffset = payload.PrimaryOffset;
            state.PrimaryMaster = payload.PrimaryMaster;
            state.CdlMaster = payload.CdlMaster;
            state.WhitePoint = payload.WhitePoint;
            state.GreyOut = payload.GreyOut;
            state.ShoulderPower = payload.ShoulderPower;
            state.ToePower = payload.ToePower;
            state.ToeStops = payload.ToeStops;
            state.GamutCompressionEnabled = payload.GamutCompressionEnabled;
            state.GamutCompressionStrength = payload.GamutCompressionStrength;
            LoadCurve(state.MasterCurve, payload.MasterCurve);
            LoadCurve(state.RedCurve, payload.RedCurve);
            LoadCurve(state.GreenCurve, payload.GreenCurve);
            LoadCurve(state.BlueCurve, payload.BlueCurve);
            state.HueVsHueCurve.Reset();
            state.HueVsSaturationCurve.Reset();
            state.HueVsLuminanceCurve.Reset();
            state.LuminanceVsSaturationCurve.Reset();
            state.SaturationVsSaturationCurve.Reset();
            LoadCurve(state.HueVsHueCurve, payload.HueVsHueCurve);
            LoadCurve(state.HueVsSaturationCurve, payload.HueVsSaturationCurve);
            LoadCurve(state.HueVsLuminanceCurve, payload.HueVsLuminanceCurve);
            LoadCurve(state.LuminanceVsSaturationCurve, payload.LuminanceVsSaturationCurve);
            LoadCurve(state.SaturationVsSaturationCurve, payload.SaturationVsSaturationCurve);
            state.Qualifier.Reset();
            LoadQualifier(state.Qualifier, payload.Qualifier);
            state.ClearLut();
            if (!string.IsNullOrWhiteSpace(payload.LutPath))
            {
                if (state.LoadLut(payload.LutPath, out string lutError))
                {
                    state.LutIntensity = payload.LutIntensity;
                    state.LutColorSpace = (ColorGradeLutColorSpace)payload.LutColorSpace;
                }
                else
                {
                    Debug.LogWarning($"[ColorGrade] Lut не загружен: {lutError}");
                }
            }
            state.EnabledMask = payload.EnabledMask;
            state.ClearPreviewOverrides();
            state.Sanitize();
            ColorGradeZonePayloads.Into(
                zones,
                payload.ZonesEnabled,
                payload.Zones);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось загрузить {Path}: {exception.Message}");
            return false;
        }
    }

    public static bool ExportCdl(ColorGradeState state)
    {
        state.Sanitize();
        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<ColorDecisionList xmlns=\"urn:ASC:CDL:v1.01\">");
        builder.AppendLine("  <ColorDecision>");
        builder.AppendLine("    <ColorCorrection id=\"kern\">");
        builder.AppendLine("      <SOPNode>");
        builder.AppendLine($"        <Slope>{Triplet(state.Slope, culture)}</Slope>");
        builder.AppendLine($"        <Offset>{Triplet(state.Offset, culture)}</Offset>");
        builder.AppendLine($"        <Power>{Triplet(state.Power, culture)}</Power>");
        builder.AppendLine("      </SOPNode>");
        builder.AppendLine("      <SatNode>");
        builder.AppendLine(
            $"        <Saturation>{state.CdlSaturation.ToString("F6", culture)}</Saturation>");
        builder.AppendLine("      </SatNode>");
        builder.AppendLine("    </ColorCorrection>");
        builder.AppendLine("  </ColorDecision>");
        builder.AppendLine("</ColorDecisionList>");

        try
        {
            WriteAtomically(CdlPath, builder.ToString());
            Debug.Log($"[ColorGrade] ASC CDL -> {CdlPath}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось экспортировать {CdlPath}: {exception.Message}");
            return false;
        }
    }

    private static string Triplet(Vector3 value, CultureInfo culture) =>
        string.Concat(
            value.x.ToString("F6", culture), " ",
            value.y.ToString("F6", culture), " ",
            value.z.ToString("F6", culture));



    private static CurvePayload ToCurvePayload(ColorGradeCurve curve) => new()
    {
        Interpolation = (int)curve.Interpolation,
        Points = ToPointArray(curve),
    };

    private static Vector2[] ToPointArray(ColorGradeCurve curve)
    {
        var points = new Vector2[curve.PointCount];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = curve.GetPoint(index);
        }

        return points;
    }

    private static void LoadCurve(ColorGradeCurve target, CurvePayload? payload)
    {
        target.Load(payload?.Points, payload?.Interpolation ?? (int)ColorCurveInterpolation.Smooth);
    }

    private static QualifierPayload ToQualifierPayload(ColorGradeQualifier qualifier) => new()
    {
        Enabled = qualifier.Enabled,
        Invert = qualifier.Invert,
        HueCenter = qualifier.HueCenter,
        HueWidth = qualifier.HueWidth,
        HueSoftness = qualifier.HueSoftness,
        SaturationCenter = qualifier.SaturationCenter,
        SaturationWidth = qualifier.SaturationWidth,
        SaturationSoftness = qualifier.SaturationSoftness,
        LuminanceCenter = qualifier.LuminanceCenter,
        LuminanceWidth = qualifier.LuminanceWidth,
        LuminanceSoftness = qualifier.LuminanceSoftness,
        HueShift = qualifier.HueShift,
        Saturation = qualifier.Saturation,
        Exposure = qualifier.Exposure,
        Temperature = qualifier.Temperature,
        Tint = qualifier.Tint,
        Lift = qualifier.Lift,
        Gamma = qualifier.Gamma,
        Gain = qualifier.Gain,
        HueSamples = CopyHueSamples(qualifier),
    };

    private static void LoadQualifier(ColorGradeQualifier target, QualifierPayload? payload)
    {
        if (payload == null)
        {
            return;
        }

        target.Enabled = payload.Enabled;
        target.Invert = payload.Invert;
        target.HueCenter = payload.HueCenter;
        target.HueWidth = payload.HueWidth;
        target.HueSoftness = payload.HueSoftness;
        target.SaturationCenter = payload.SaturationCenter;
        target.SaturationWidth = payload.SaturationWidth;
        target.SaturationSoftness = payload.SaturationSoftness;
        target.LuminanceCenter = payload.LuminanceCenter;
        target.LuminanceWidth = payload.LuminanceWidth;
        target.LuminanceSoftness = payload.LuminanceSoftness;
        target.HueShift = payload.HueShift;
        target.Saturation = payload.Saturation;
        target.Exposure = payload.Exposure;
        target.Temperature = payload.Temperature;
        target.Tint = payload.Tint;
        target.Lift = payload.Lift;
        target.Gamma = payload.Gamma;
        target.Gain = payload.Gain;
        target.ClearHueSamples();
        if (payload.HueSamples != null)
        {
            foreach (float sample in payload.HueSamples)
            {
                target.AddHueSample(sample);
            }
        }
    }

    private static float[] CopyHueSamples(ColorGradeQualifier qualifier)
    {
        float[] samples = new float[qualifier.HueSamples.Count];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = qualifier.HueSamples[index];
        }

        return samples;
    }



    private static void WriteAtomically(string path, string contents)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception cleanupException)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Не удалось удалить временный файл " +
                    $"{temporaryPath}: {cleanupException.Message}");
            }
        }
    }

    private static string SanitizePresetName(string name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            bool forbidden = Array.IndexOf(invalid, character) >= 0 ||
                char.IsControl(character) ||
                character == '/' ||
                character == '\\';
            if (!forbidden)
            {
                builder.Append(character);
            }
        }

        string sanitized = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    public static string ToLookSource(ColorGradeState state)
    {
        state.Sanitize();
        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("    public static class ColorGrading");
        builder.AppendLine("    {");
        Constant(builder, culture, "Exposure", state.Exposure);
        Constant(builder, culture, "Contrast", state.Contrast);
        Constant(builder, culture, "Pivot", state.Pivot);
        Constant(builder, culture, "Shadows", state.Shadows);
        Constant(builder, culture, "Highlights", state.Highlights);
        Constant(builder, culture, "Blacks", state.Blacks);
        Constant(builder, culture, "Whites", state.Whites);
        Constant(builder, culture, "Toe", state.Toe);
        Constant(builder, culture, "Shoulder", state.Shoulder);
        Constant(builder, culture, "Saturation", state.Saturation);
        Constant(builder, culture, "CdlSaturation", state.CdlSaturation);
        Constant(builder, culture, "Vibrance", state.Vibrance);
        Constant(builder, culture, "Hue", state.Hue);
        AppendCurveSource(builder, culture, "HueVsHueCurve", state.HueVsHueCurve);
        AppendCurveSource(builder, culture, "HueVsSaturationCurve", state.HueVsSaturationCurve);
        AppendCurveSource(builder, culture, "HueVsLuminanceCurve", state.HueVsLuminanceCurve);
        AppendCurveSource(
            builder,
            culture,
            "LuminanceVsSaturationCurve",
            state.LuminanceVsSaturationCurve);
        AppendCurveSource(
            builder,
            culture,
            "SaturationVsSaturationCurve",
            state.SaturationVsSaturationCurve);
        builder.AppendLine();
        builder.AppendLine("        public static Color Filter => Color.white;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static class Grade");
        builder.AppendLine("    {");
        builder.AppendLine(
            $"        public const DisplayTransform Transform = DisplayTransform.{state.Transform};");
        Constant(builder, culture, "WhitePoint", state.WhitePoint);
        Constant(builder, culture, "Temperature", state.Temperature);
        Constant(builder, culture, "Tint", state.Tint);
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Slope => {VectorSource(state.Slope, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Offset => {VectorSource(state.Offset, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 Power => {VectorSource(state.Power, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryLift => {VectorSource(state.PrimaryLift, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryGamma => {VectorSource(state.PrimaryGamma, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryGain => {VectorSource(state.PrimaryGain, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 PrimaryOffset => {VectorSource(state.PrimaryOffset, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector4 PrimaryMaster => {VectorSource(state.PrimaryMaster, culture)};");
        builder.AppendLine();
        builder.AppendLine($"        public static Vector3 CdlMaster => {VectorSource(state.CdlMaster, culture)};");
        builder.AppendLine();
        Constant(builder, culture, "GreyOut", state.GreyOut);
        Constant(builder, culture, "ShoulderPower", state.ShoulderPower);
        Constant(builder, culture, "ToePower", state.ToePower);
        Constant(builder, culture, "ToeStops", state.ToeStops);
        builder.AppendLine("    }");
        return builder.ToString();
    }

    private static void Constant(StringBuilder builder, CultureInfo culture, string name, float value) =>
        builder.AppendLine($"        public const float {name} = {value.ToString("0.######", culture)}f;");

    private static string VectorSource(Vector3 value, CultureInfo culture) =>
        string.Concat(
            "new(", value.x.ToString("0.######", culture), "f, ",
            value.y.ToString("0.######", culture), "f, ",
            value.z.ToString("0.######", culture), "f)");

    private static string VectorSource(Vector4 value, CultureInfo culture) =>
        string.Concat(
            "new(", value.x.ToString("0.######", culture), "f, ",
            value.y.ToString("0.######", culture), "f, ",
            value.z.ToString("0.######", culture), "f, ",
            value.w.ToString("0.######", culture), "f)");

    private static void AppendCurveSource(
        StringBuilder builder,
        CultureInfo culture,
        string name,
        ColorGradeCurve curve)
    {
        builder.AppendLine($"        public static Vector2[] {name} => new Vector2[]");
        builder.AppendLine("        {");
        for (int index = 0; index < curve.PointCount; index++)
        {
            Vector2 point = curve.GetPoint(index);
            string x = point.x.ToString("F6", culture);
            string y = point.y.ToString("F6", culture);
            builder.AppendLine(
                $"            new Vector2({x}f, {y}f),");
        }

        builder.AppendLine("        };");
    }
}
