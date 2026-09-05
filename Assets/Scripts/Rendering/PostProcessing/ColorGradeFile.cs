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
    private const int CurrentVersion = 3;
    private const string FileName = "color_grade.json";

    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static string CdlPath =>
        System.IO.Path.Combine(Application.persistentDataPath, "color_grade.cdl");

    [Serializable]
    private sealed class Payload
    {
        public int Version;
        public int Transform;
        public float Exposure;
        public float Contrast;
        public float Saturation;
        public float Temperature;
        public float Tint;
        public Vector3 Slope;
        public Vector3 Offset;
        public Vector3 Power;
        public float WhitePoint;
        public float GreyOut;
        public float CurveSlope;
        public float ShoulderPower;
        public float ToePower;
        public float ToeStops;
        public float PathToWhiteAmount;
        public float PathToWhitePower;
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
            Saturation = state.Saturation,
            Temperature = state.Temperature,
            Tint = state.Tint,
            Slope = state.Slope,
            Offset = state.Offset,
            Power = state.Power,
            WhitePoint = state.WhitePoint,
            GreyOut = state.GreyOut,
            CurveSlope = state.CurveSlope,
            ShoulderPower = state.ShoulderPower,
            ToePower = state.ToePower,
            ToeStops = state.ToeStops,
            PathToWhiteAmount = state.PathToWhiteAmount,
            PathToWhitePower = state.PathToWhitePower,
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
            state.Saturation = payload.Saturation;
            state.Temperature = payload.Temperature;
            state.Tint = payload.Tint;
            state.Slope = payload.Slope;
            state.Offset = payload.Offset;
            state.Power = payload.Power;
            state.WhitePoint = payload.WhitePoint;
            state.GreyOut = payload.GreyOut;
            state.CurveSlope = payload.CurveSlope;
            state.ShoulderPower = payload.ShoulderPower;
            state.ToePower = payload.ToePower;
            state.ToeStops = payload.ToeStops;
            state.PathToWhiteAmount = payload.PathToWhiteAmount;
            state.PathToWhitePower = payload.PathToWhitePower;
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
            $"        <Saturation>{state.Saturation.ToString("F6", culture)}</Saturation>");
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
        Constant(builder, culture, "Saturation", state.Saturation);
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
}
