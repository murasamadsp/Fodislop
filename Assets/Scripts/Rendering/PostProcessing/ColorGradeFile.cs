#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Хранение и вывоз грейда.
/// </summary>
/// <remarks>
/// НЕ ЧЕРЕЗ <c>ClientConfig</c>, и это решение, а не лень. Секция в конфиге
/// обошлась бы ступенью миграции схемы, списком в SettingsProbe, атрибутом
/// <c>[SettingConsumer]</c> на каждое из семнадцати полей и ключами
/// локализации на четырёх языках — вся эта обвязка существует ради настроек,
/// которые видит игрок. Грейд игрок не видит: он авторский, и его судьба —
/// переехать в <c>PostProcessLook</c> числами.
///
/// Отсюда три выхода:
///   JSON  — рабочее состояние между запусками;
///   .cdl  — обмен: ASC CDL читают сторонние инструменты цветокоррекции;
///   C#    — готовый кусок PostProcessLook, чтобы найденное попадало в код
///           копированием, а не переписыванием от руки с опечатками.
/// </remarks>
public static class ColorGradeFile
{
    private const int CurrentVersion = 20;
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
            if (!HasRequiredPayloadFields(json))
            {
                Debug.LogWarning($"[ColorGrade] Пресет {presetPath} неполон.");
                return false;
            }

            Payload? payload = JsonUtility.FromJson<Payload>(json);
            if (payload == null || payload.Version is < 1 or > CurrentVersion)
            {
                Debug.LogWarning($"[ColorGrade] Пресет {presetPath} имеет неподдерживаемую версию.");
                return false;
            }

            if (!HasRequiredVersionedFields(json, payload))
            {
                Debug.LogWarning($"[ColorGrade] Пресет {presetPath} неполон для своей версии.");
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
        public Vector4 HueVsHue = new(120f, 30f, 15f, 0f);
        public Vector4 HueVsLuminance = new(120f, 30f, 15f, 0f);
        public Vector4 LuminanceVsSaturation = new(0.5f, 0.25f, 0.1f, 1f);
        public Vector4 SaturationVsSaturation = new(0.5f, 0.25f, 0.1f, 1f);
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
        public Vector4 HueVsSaturation = new(120f, 30f, 15f, 1f);
        public float WhitePoint;
        public float BlackPoint;
        public float InputWhitePoint = 1f;
        public float HighlightRecovery;
        public float GreyOut;
        public float CurveSlope;
        public float ShoulderPower;
        public float ToePower;
        public float ToeStops;
        public float PathToWhiteAmount;
        public float PathToWhitePower;
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
        public int InputColorSpace;
        public int WorkingColorSpace;
        public int OutputColorSpace;
        public int InputTransfer;
        public int OutputTransfer = (int)ColorGradeTransferFunction.Srgb;
        public int ReferenceMode;
        public int DynamicRange = (int)ColorGradeDynamicRangeMode.Sdr;
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
            HueVsHue = state.HueVsHue,
            HueVsLuminance = state.HueVsLuminance,
            LuminanceVsSaturation = state.LuminanceVsSaturation,
            SaturationVsSaturation = state.SaturationVsSaturation,
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
            HueVsSaturation = state.HueVsSaturation,
            WhitePoint = state.WhitePoint,
            BlackPoint = state.BlackPoint,
            InputWhitePoint = state.InputWhitePoint,
            HighlightRecovery = state.HighlightRecovery,
            GreyOut = state.GreyOut,
            CurveSlope = state.CurveSlope,
            ShoulderPower = state.ShoulderPower,
            ToePower = state.ToePower,
            ToeStops = state.ToeStops,
            PathToWhiteAmount = state.PathToWhiteAmount,
            PathToWhitePower = state.PathToWhitePower,
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
            InputColorSpace = (int)state.ColorManagement.InputColorSpace,
            WorkingColorSpace = (int)state.ColorManagement.WorkingColorSpace,
            OutputColorSpace = (int)state.ColorManagement.OutputColorSpace,
            InputTransfer = (int)state.ColorManagement.InputTransfer,
            OutputTransfer = (int)state.ColorManagement.OutputTransfer,
            ReferenceMode = (int)state.ColorManagement.ReferenceMode,
            DynamicRange = (int)state.ColorManagement.DynamicRange,
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

    /// <summary>
    /// Читает грейд с диска. Возвращает <c>false</c>, если файла нет — это не
    /// ошибка, а обычное состояние до первого сохранения.
    /// </summary>
    public static bool TryLoad(ColorGradeState state, ColorGradeZones? zones = null)
    {
        if (!File.Exists(Path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(Path);
            if (!HasRequiredPayloadFields(json))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} неполон; грейд оставлен как есть.");
                return false;
            }

            Payload? payload = JsonUtility.FromJson<Payload>(json);
            if (payload == null)
            {
                Debug.LogWarning($"[ColorGrade] Файл {Path} не разобран; грейд оставлен как есть.");
                return false;
            }

            // Version обязателен. JsonUtility заполняет отсутствующие поля
            // нулями, поэтому принятие безверсионного `{}` выглядело бы как
            // успешная загрузка, но обесцвечивало кадр и прижимало кривую к
            // минимальным границам.
            if (payload.Version is < 1 or > CurrentVersion)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Версия файла {payload.Version} не поддерживается; " +
                    "грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 4 &&
                CountJsonFields(json, nameof(Payload.HueVsSaturation)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "HueVsSaturation; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 7 &&
                CountJsonFields(json, nameof(Payload.Vibrance)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "Vibrance; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 8 &&
                (CountJsonFields(json, nameof(Payload.HueVsHue)) == 0 ||
                 CountJsonFields(json, nameof(Payload.HueVsLuminance)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "полные selective hue-кривые; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 9 &&
                (CountJsonFields(json, nameof(Payload.Pivot)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Shadows)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Highlights)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Blacks)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Whites)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Toe)) == 0 ||
                 CountJsonFields(json, nameof(Payload.Shoulder)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "полный контрастный блок; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 10 && CountJsonFields(json, nameof(Payload.Hue)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит Hue; " +
                    "грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 11 &&
                (CountJsonFields(json, nameof(Payload.LuminanceVsSaturation)) == 0 ||
                 CountJsonFields(json, nameof(Payload.SaturationVsSaturation)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "полные luma/saturation curves; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 12 &&
                (CountJsonFields(json, nameof(Payload.MasterCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.RedCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.GreenCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.BlueCurve)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "полный набор RGB/luma curves; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 17 &&
                (CountJsonFields(json, nameof(Payload.HueVsHueCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.HueVsSaturationCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.HueVsLuminanceCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.LuminanceVsSaturationCurve)) == 0 ||
                 CountJsonFields(json, nameof(Payload.SaturationVsSaturationCurve)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "selective curves; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 18 &&
                (CountJsonFields(json, nameof(Payload.PrimaryLift)) == 0 ||
                 CountJsonFields(json, nameof(Payload.PrimaryGamma)) == 0 ||
                 CountJsonFields(json, nameof(Payload.PrimaryGain)) == 0 ||
                 CountJsonFields(json, nameof(Payload.PrimaryOffset)) == 0 ||
                 CountJsonFields(json, nameof(Payload.PrimaryMaster)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "primary wheels; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 19 &&
                CountJsonFields(json, nameof(Payload.EnabledMask)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "enabled mask; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 20 &&
                CountJsonFields(json, nameof(Payload.DynamicRange)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "dynamic range; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 13 &&
                CountJsonFields(json, nameof(Payload.Qualifier)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит qualifier; " +
                    "грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 5 &&
                (CountJsonFields(json, nameof(Payload.BlackPoint)) == 0 ||
                 CountJsonFields(json, nameof(Payload.InputWhitePoint)) == 0 ||
                 CountJsonFields(json, nameof(Payload.HighlightRecovery)) == 0))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "полный входной диапазон; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 6 &&
                CountJsonFields(json, nameof(Payload.CdlMaster)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "CdlMaster; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 16 &&
                CountJsonFields(json, nameof(Payload.CdlSaturation)) == 0)
            {
                Debug.LogWarning(
                    $"[ColorGrade] Файл {Path} версии {payload.Version} не содержит " +
                    "CDL saturation; грейд оставлен как есть.");
                return false;
            }

            if (payload.Version >= 2 &&
                !HasRequiredZonePayloadFields(json, payload.Zones, payload.Version))
            {
                Debug.LogWarning(
                    $"[ColorGrade] Секция зон в {Path} неполна; грейд оставлен как есть.");
                return false;
            }

            state.Transform = (DisplayTransform)payload.Transform;
            state.Exposure = payload.Exposure;
            state.Contrast = payload.Contrast;
            state.Pivot = payload.Version >= 9 ? payload.Pivot : 0.5f;
            state.Shadows = payload.Version >= 9 ? payload.Shadows : 0f;
            state.Highlights = payload.Version >= 9 ? payload.Highlights : 0f;
            state.Blacks = payload.Version >= 9 ? payload.Blacks : 0f;
            state.Whites = payload.Version >= 9 ? payload.Whites : 0f;
            state.Toe = payload.Version >= 9 ? payload.Toe : 0f;
            state.Shoulder = payload.Version >= 9 ? payload.Shoulder : 0f;
            state.Saturation = payload.Saturation;
            state.CdlSaturation = payload.Version >= 16 ? payload.CdlSaturation : 1f;
            state.Vibrance = payload.Version >= 7 ? payload.Vibrance : 0f;
            state.Hue = payload.Version >= 10 ? payload.Hue : 0f;
            state.HueVsHue = payload.Version >= 8
                ? payload.HueVsHue
                : new Vector4(120f, 30f, 15f, 0f);
            state.HueVsLuminance = payload.Version >= 8
                ? payload.HueVsLuminance
                : new Vector4(120f, 30f, 15f, 0f);
            state.LuminanceVsSaturation = payload.Version >= 11
                ? payload.LuminanceVsSaturation
                : new Vector4(0.5f, 0.25f, 0.1f, 1f);
            state.SaturationVsSaturation = payload.Version >= 11
                ? payload.SaturationVsSaturation
                : new Vector4(0.5f, 0.25f, 0.1f, 1f);
            state.Temperature = payload.Temperature;
            state.Tint = payload.Tint;
            state.Slope = payload.Slope;
            state.Offset = payload.Offset;
            state.Power = payload.Power;
            state.PrimaryLift = payload.Version >= 18 ? payload.PrimaryLift : Vector3.zero;
            state.PrimaryGamma = payload.Version >= 18 ? payload.PrimaryGamma : Vector3.one;
            state.PrimaryGain = payload.Version >= 18 ? payload.PrimaryGain : Vector3.one;
            state.PrimaryOffset = payload.Version >= 18 ? payload.PrimaryOffset : Vector3.zero;
            state.PrimaryMaster = payload.Version >= 18
                ? payload.PrimaryMaster
                : new Vector4(0f, 1f, 1f, 0f);
            state.CdlMaster = payload.Version >= 6
                ? payload.CdlMaster
                : new Vector3(1f, 0f, 1f);
            state.HueVsSaturation = payload.Version >= 4
                ? payload.HueVsSaturation
                : new Vector4(120f, 30f, 15f, 1f);
            state.WhitePoint = payload.WhitePoint;
            state.BlackPoint = payload.Version >= 5 ? payload.BlackPoint : 0f;
            state.InputWhitePoint = payload.Version >= 5 ? payload.InputWhitePoint : 1f;
            state.HighlightRecovery = payload.Version >= 5 ? payload.HighlightRecovery : 0f;
            state.GreyOut = payload.GreyOut;
            state.CurveSlope = payload.CurveSlope;
            state.ShoulderPower = payload.ShoulderPower;
            state.ToePower = payload.ToePower;
            state.ToeStops = payload.ToeStops;
            state.PathToWhiteAmount = payload.PathToWhiteAmount;
            state.PathToWhitePower = payload.PathToWhitePower;
            if (payload.Version >= 12)
            {
                LoadCurve(state.MasterCurve, payload.MasterCurve);
                LoadCurve(state.RedCurve, payload.RedCurve);
                LoadCurve(state.GreenCurve, payload.GreenCurve);
                LoadCurve(state.BlueCurve, payload.BlueCurve);
            }
            state.HueVsHueCurve.Reset();
            state.HueVsSaturationCurve.Reset();
            state.HueVsLuminanceCurve.Reset();
            state.LuminanceVsSaturationCurve.Reset();
            state.SaturationVsSaturationCurve.Reset();
            if (payload.Version >= 17)
            {
                LoadCurve(state.HueVsHueCurve, payload.HueVsHueCurve);
                LoadCurve(state.HueVsSaturationCurve, payload.HueVsSaturationCurve);
                LoadCurve(state.HueVsLuminanceCurve, payload.HueVsLuminanceCurve);
                LoadCurve(state.LuminanceVsSaturationCurve, payload.LuminanceVsSaturationCurve);
                LoadCurve(state.SaturationVsSaturationCurve, payload.SaturationVsSaturationCurve);
            }
            state.Qualifier.Reset();
            if (payload.Version >= 13)
            {
                LoadQualifier(state.Qualifier, payload.Qualifier);
            }
            state.ClearLut();
            if (payload.Version >= 13 &&
                !string.IsNullOrWhiteSpace(payload.LutPath))
            {
                if (state.LoadLut(payload.LutPath, out string lutError))
                {
                    state.LutIntensity = payload.LutIntensity;
                    state.LutColorSpace = (ColorGradeLutColorSpace)payload.LutColorSpace;
                }
                else
                {
                    Debug.LogWarning($"[ColorGrade] LUT не загружен: {lutError}");
                }
            }
            state.ColorManagement.InputColorSpace = payload.Version >= 14
                ? (ColorGradeColorSpace)payload.InputColorSpace
                : ColorGradeColorSpace.Rec709;
            state.ColorManagement.WorkingColorSpace = payload.Version >= 14
                ? (ColorGradeColorSpace)payload.WorkingColorSpace
                : ColorGradeColorSpace.Rec709;
            state.ColorManagement.OutputColorSpace = payload.Version >= 14
                ? (ColorGradeColorSpace)payload.OutputColorSpace
                : ColorGradeColorSpace.Rec709;
            state.ColorManagement.InputTransfer = payload.Version >= 14
                ? (ColorGradeTransferFunction)payload.InputTransfer
                : ColorGradeTransferFunction.Linear;
            state.ColorManagement.OutputTransfer = payload.Version >= 14
                ? (ColorGradeTransferFunction)payload.OutputTransfer
                : ColorGradeTransferFunction.Srgb;
            state.ColorManagement.ReferenceMode = payload.Version >= 14
                ? (ColorGradeReferenceMode)payload.ReferenceMode
                : ColorGradeReferenceMode.SceneReferred;
            state.ColorManagement.DynamicRange = payload.Version >= 20
                ? (ColorGradeDynamicRangeMode)payload.DynamicRange
                : ColorGradeDynamicRangeMode.Sdr;
            state.EnabledMask = payload.Version >= 19
                ? payload.EnabledMask
                : (1 << 6) - 1;
            state.ClearPreviewOverrides();
            state.Sanitize();
            ColorGradeZonePayloads.Into(
                zones,
                payload.ZonesEnabled,
                payload.Zones,
                payload.Version);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ColorGrade] Не удалось загрузить {Path}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Вывозит ASC CDL. Формат описан ASC и читается сторонними инструментами,
    /// поэтому числа пишутся инвариантной культурой: с запятой вместо точки
    /// файл не примет никто.
    /// </summary>
    public static bool ExportCdl(ColorGradeState state)
    {
        state.Sanitize();
        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<ColorDecisionList xmlns=\"urn:ASC:CDL:v1.01\">");
        builder.AppendLine("  <ColorDecision>");
        builder.AppendLine("    <ColorCorrection id=\"fodinae\">");
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

    private static bool HasRequiredPayloadFields(string json)
    {
        // JsonUtility не отличает отсутствующее поле от честного нуля. Без
        // этой проверки оборванный, но синтаксически валидный JSON вроде
        // { "Version": 2 } успешно загружался и заменял почти весь look
        // минимальными значениями после Sanitize().
        string[] requiredFields =
        [
            nameof(Payload.Version),
            nameof(Payload.Transform),
            nameof(Payload.Exposure),
            nameof(Payload.Contrast),
            nameof(Payload.Saturation),
            nameof(Payload.Temperature),
            nameof(Payload.Tint),
            nameof(Payload.Slope),
            nameof(Payload.Offset),
            nameof(Payload.Power),
            nameof(Payload.WhitePoint),
            nameof(Payload.GreyOut),
            nameof(Payload.CurveSlope),
            nameof(Payload.ShoulderPower),
            nameof(Payload.ToePower),
            nameof(Payload.ToeStops),
            nameof(Payload.PathToWhiteAmount),
            nameof(Payload.PathToWhitePower),
        ];

        foreach (string field in requiredFields)
        {
            if (CountJsonFields(json, field) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRequiredVersionedFields(string json, Payload payload)
    {
        if (payload.Version >= 4 &&
            CountJsonFields(json, nameof(Payload.HueVsSaturation)) == 0)
        {
            return false;
        }

        if (payload.Version >= 7 && CountJsonFields(json, nameof(Payload.Vibrance)) == 0)
        {
            return false;
        }

        if (payload.Version >= 8 &&
            (CountJsonFields(json, nameof(Payload.HueVsHue)) == 0 ||
             CountJsonFields(json, nameof(Payload.HueVsLuminance)) == 0))
        {
            return false;
        }

        if (payload.Version >= 9 &&
            (CountJsonFields(json, nameof(Payload.Pivot)) == 0 ||
             CountJsonFields(json, nameof(Payload.Shadows)) == 0 ||
             CountJsonFields(json, nameof(Payload.Highlights)) == 0 ||
             CountJsonFields(json, nameof(Payload.Blacks)) == 0 ||
             CountJsonFields(json, nameof(Payload.Whites)) == 0 ||
             CountJsonFields(json, nameof(Payload.Toe)) == 0 ||
             CountJsonFields(json, nameof(Payload.Shoulder)) == 0))
        {
            return false;
        }

        if (payload.Version >= 10 && CountJsonFields(json, nameof(Payload.Hue)) == 0)
        {
            return false;
        }

        if (payload.Version >= 11 &&
            (CountJsonFields(json, nameof(Payload.LuminanceVsSaturation)) == 0 ||
             CountJsonFields(json, nameof(Payload.SaturationVsSaturation)) == 0))
        {
            return false;
        }

        if (payload.Version >= 12 &&
            (CountJsonFields(json, nameof(Payload.MasterCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.RedCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.GreenCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.BlueCurve)) == 0))
        {
            return false;
        }

        if (payload.Version >= 5 &&
            (CountJsonFields(json, nameof(Payload.BlackPoint)) == 0 ||
             CountJsonFields(json, nameof(Payload.InputWhitePoint)) == 0 ||
             CountJsonFields(json, nameof(Payload.HighlightRecovery)) == 0))
        {
            return false;
        }

        if (payload.Version >= 6 && CountJsonFields(json, nameof(Payload.CdlMaster)) == 0)
        {
            return false;
        }

        if (payload.Version >= 13 && CountJsonFields(json, nameof(Payload.Qualifier)) == 0)
        {
            return false;
        }

        if (payload.Version >= 16 && CountJsonFields(json, nameof(Payload.CdlSaturation)) == 0)
        {
            return false;
        }

        if (payload.Version >= 17 &&
            (CountJsonFields(json, nameof(Payload.HueVsHueCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.HueVsSaturationCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.HueVsLuminanceCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.LuminanceVsSaturationCurve)) == 0 ||
             CountJsonFields(json, nameof(Payload.SaturationVsSaturationCurve)) == 0))
        {
            return false;
        }

        if (payload.Version >= 18 &&
            (CountJsonFields(json, nameof(Payload.PrimaryLift)) == 0 ||
             CountJsonFields(json, nameof(Payload.PrimaryGamma)) == 0 ||
             CountJsonFields(json, nameof(Payload.PrimaryGain)) == 0 ||
             CountJsonFields(json, nameof(Payload.PrimaryOffset)) == 0 ||
             CountJsonFields(json, nameof(Payload.PrimaryMaster)) == 0))
        {
            return false;
        }

        if (payload.Version >= 19 && CountJsonFields(json, nameof(Payload.EnabledMask)) == 0)
        {
            return false;
        }

        if (payload.Version >= 20 && CountJsonFields(json, nameof(Payload.DynamicRange)) == 0)
        {
            return false;
        }

        return HasRequiredZonePayloadFields(json, payload.Zones, payload.Version);
    }

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

    private static bool HasRequiredZonePayloadFields(
        string json,
        ColorGradeZonePayload[]? zones,
        int payloadVersion)
    {
        if (zones == null ||
            CountJsonFields(json, nameof(Payload.ZonesEnabled)) == 0 ||
            CountJsonFields(json, nameof(Payload.Zones)) == 0)
        {
            return false;
        }

        string[] zoneOnlyFields =
        [
            nameof(ColorGradeZonePayload.Name),
            nameof(ColorGradeZonePayload.CenterY),
            nameof(ColorGradeZonePayload.HalfHeight),
            nameof(ColorGradeZonePayload.Feather),
        ];
        foreach (string field in zoneOnlyFields)
        {
            if (CountJsonFields(json, field) < zones.Length)
            {
                return false;
            }
        }

        // Эти имена один раз уже есть у базового look, поэтому ожидается
        // базовое поле плюс поле каждой зоны. Так валидный, но оборванный JSON
        // не превращает хвост зоны в нулевые значения JsonUtility.
        if (zones.Length > 0)
        {
            int expected = zones.Length + 1;
            string[] gradeFields =
            [
                nameof(ColorGradeZonePayload.Transform),
                nameof(ColorGradeZonePayload.Temperature),
                nameof(ColorGradeZonePayload.Tint),
                nameof(ColorGradeZonePayload.Slope),
                nameof(ColorGradeZonePayload.Offset),
                nameof(ColorGradeZonePayload.Power),
                nameof(ColorGradeZonePayload.WhitePoint),
                nameof(ColorGradeZonePayload.GreyOut),
                nameof(ColorGradeZonePayload.CurveSlope),
                nameof(ColorGradeZonePayload.ShoulderPower),
                nameof(ColorGradeZonePayload.ToePower),
                nameof(ColorGradeZonePayload.ToeStops),
                nameof(ColorGradeZonePayload.PathToWhiteAmount),
                nameof(ColorGradeZonePayload.PathToWhitePower),
            ];
            foreach (string field in gradeFields)
            {
                if (CountJsonFields(json, field) < expected)
                {
                    return false;
                }
            }

            // v3 добавила отсутствовавшие раньше параметры полного грейда.
            if (payloadVersion >= 3 &&
                (CountJsonFields(json, nameof(ColorGradeZonePayload.Exposure)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.Contrast)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.Saturation)) < expected))
            {
                return false;
            }

            if (payloadVersion >= 18 &&
                (CountJsonFields(json, nameof(ColorGradeZonePayload.PrimaryLift)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.PrimaryGamma)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.PrimaryGain)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.PrimaryOffset)) < expected ||
                 CountJsonFields(json, nameof(ColorGradeZonePayload.PrimaryMaster)) < expected))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountJsonFields(string json, string field)
    {
        string token = $"\"{field}\"";
        int count = 0;
        int start = 0;
        while (start < json.Length)
        {
            int index = json.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            int afterToken = index + token.Length;
            while (afterToken < json.Length && char.IsWhiteSpace(json[afterToken]))
            {
                afterToken++;
            }

            if (afterToken < json.Length && json[afterToken] == ':')
            {
                count++;
            }

            start = index + token.Length;
        }

        return count;
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

    /// <summary>
    /// Печатает найденное куском <c>PostProcessLook.Grade</c>: копируется в
    /// код целиком, без переписывания чисел от руки.
    /// </summary>
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
        builder.AppendLine($"        public static Vector4 HueVsHue => {VectorSource(state.HueVsHue, culture)};");
        builder.AppendLine($"        public static Vector4 HueVsLuminance => {VectorSource(state.HueVsLuminance, culture)};");
        builder.AppendLine($"        public static Vector4 LuminanceVsSaturation => {VectorSource(state.LuminanceVsSaturation, culture)};");
        builder.AppendLine($"        public static Vector4 SaturationVsSaturation => {VectorSource(state.SaturationVsSaturation, culture)};");
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
        builder.AppendLine(
            $"        public static Vector4 HueVsSaturation => {VectorSource(state.HueVsSaturation, culture)};");
        builder.AppendLine();
        Constant(builder, culture, "GreyOut", state.GreyOut);
        Constant(builder, culture, "CurveSlope", state.CurveSlope);
        Constant(builder, culture, "ShoulderPower", state.ShoulderPower);
        Constant(builder, culture, "ToePower", state.ToePower);
        Constant(builder, culture, "ToeStops", state.ToeStops);
        Constant(builder, culture, "PathToWhiteAmount", state.PathToWhiteAmount);
        Constant(builder, culture, "PathToWhitePower", state.PathToWhitePower);
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
