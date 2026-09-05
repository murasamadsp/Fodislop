#nullable enable

using Fodinae.Tools.Imgui;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing.Workbench;

/// <summary>Зоны грейда по высоте: объявление и снятие с текущей позиции.</summary>
/// <remarks>
/// РАБОТА ИДЁТ ОТ КАМЕРЫ, А НЕ ОТ ЧИСЕЛ. Высота зоны не набирается с
/// клавиатуры: автор приводит камеру туда, где вид должен быть таким, крутит
/// грейд в соседнем окне и нажимает «снять сюда». Числа он увидит потом, в
/// сохранённом файле. Обратный порядок — сначала придумать высоту, потом
/// проверить — означает угадывание, а угадывать в цвете нечего.
/// </remarks>
internal sealed class GradingZonesWindow : ToolWindow
{
    private const float DefaultHalfHeight = 24f;
    private const float DefaultFeather = 16f;

    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private Vector2 _scroll;
    private int _nextIndex = 1;
    private int _removeRequested = -1;
    private bool _clearRequested;
    private ColorGradeZone? _addRequested;

    public GradingZonesWindow(ColorGradeState state, ColorGradeZones zones)
        : base("Зоны тонкоррекции", new Rect(16f, 382f, 260f, 450f))
    {
        _state = state;
        _zones = zones;
        Visible = false;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 340f);

    /// <summary>
    /// Whether the authored state is currently applied to the frame and can
    /// therefore be captured without saving a look different from the preview.
    /// </summary>
    public bool CaptureEnabled { get; set; }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextIndex = 1;
        _removeRequested = -1;
        _clearRequested = false;
        _addRequested = null;
    }

    protected override void DrawContent()
    {
        ApplyPendingChanges();

        // Не Camera.main: правило проекта запрещает искать камеру по тегу, и
        // правильно запрещает — здесь нужна ровно та камера, по которой
        // считается кадр, а её проходу уже толкнули снаружи.
        Camera? camera = PostProcessRuntimeState.MainCamera;
        float cameraY = camera != null ? camera.transform.position.y : float.NaN;

        GUILayout.Label("ПРИВЯЗКА К ВЫСОТЕ", SectionLabelStyle);
        string zonesMarker = _zones.Enabled ? "●" : "○";
        _zones.Enabled = GUILayout.Toggle(
            _zones.Enabled,
            $"{zonesMarker}  Зоны действуют",
            SegmentedButtonStyle);
        GUILayout.Label(
            camera != null
                ? _zones.Enabled
                    ? $"Камера Y: {cameraY:F1}   —   {_zones.DescribeAt(cameraY)}"
                    : $"Камера Y: {cameraY:F1}   —   зоны выключены, действует база"
                : "Камеры нет: зона не определяется.",
            camera != null ? MutedLabelStyle : ToolTheme.WarningLabel);

        using (new GUILayout.HorizontalScope())
        {
            bool controlsEnabled = GUI.enabled;
            GUI.enabled = controlsEnabled && camera != null && CaptureEnabled;
            if (GUILayout.Button("Снять грейд сюда", ActiveButtonStyle))
            {
                _state.Sanitize();
                _addRequested = new ColorGradeZone
                {
                    Name = NextZoneName(),
                    CenterY = cameraY,
                    HalfHeight = DefaultHalfHeight,
                    Feather = DefaultFeather,
                    Exposure = _state.Exposure,
                    Contrast = _state.Contrast,
                    Saturation = _state.Saturation,
                    Grade = _state.ToAuthoredSnapshot(),
                };
            }

            GUI.enabled = controlsEnabled;
            if (GUILayout.Button("Очистить", DangerButtonStyle))
            {
                _clearRequested = true;
            }
        }

        if (!CaptureEnabled)
        {
            GUILayout.Label(
                "Чтобы снять грейд в зону, откройте окно «Тонкоррекция»: " +
                "сохраняться должен именно показанный кадр.",
                ToolTheme.WarningLabel);
        }

        if (_state.HasPreviewOverrides)
        {
            GUILayout.Label(
                "Соло/обход — только просмотр; зона сохранит полный грейд.",
                ToolTheme.WarningLabel);
        }

        ToolTheme.Separator();
        GUILayout.Label($"СОХРАНЁННЫЕ ЗОНЫ  ·  {_zones.Count}", SectionLabelStyle);
        using var scroll = new GUILayout.ScrollViewScope(_scroll);
        _scroll = scroll.scrollPosition;

        if (_zones.Count == 0)
        {
            GUILayout.Label(
                "Зон нет — работает единый грейд. Он же остаётся основой: " +
                "зоны накладываются поверх, поэтому дыра между ними не " +
                "оставляет кадр без кривой.",
                MutedLabelStyle);
            return;
        }

        for (int index = 0; index < _zones.Count; index++)
        {
            DrawZone(index, cameraY);
        }
    }

    private void DrawZone(int index, float cameraY)
    {
        ColorGradeZone zone = _zones.Zones[index];
        using var box = new GUILayout.VerticalScope(CardStyle);

        using (new GUILayout.HorizontalScope())
        {
            float weight = zone.WeightAt(cameraY);
            GUILayout.Label($"{zone.Name}  ·  вес {weight:P0}", SectionLabelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", DangerButtonStyle, GUILayout.Width(28f)))
            {
                _removeRequested = index;
            }
        }

        GUILayout.Label($"Центр Y  {zone.CenterY:F1}", MutedLabelStyle);
        GUILayout.Label(
            $"эксп {zone.Exposure:+0.##;-0.##;0}   " +
            $"контраст {zone.Contrast:+0.##;-0.##;0}   " +
            $"цвет {zone.Saturation:0.##}",
            MutedLabelStyle);
        float half;
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(
                $"Ядро ±{zone.HalfHeight:F1}",
                ToolTheme.FieldLabel,
                GUILayout.Width(112f));
            half = GUILayout.HorizontalSlider(zone.HalfHeight, 0f, 256f);
        }

        float feather;
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(
                $"Переход {zone.Feather:F1}",
                ToolTheme.FieldLabel,
                GUILayout.Width(112f));
            feather = GUILayout.HorizontalSlider(zone.Feather, 0f, 256f);
        }

        if (!Mathf.Approximately(half, zone.HalfHeight) ||
            !Mathf.Approximately(feather, zone.Feather))
        {
            _zones.Replace(index, zone with { HalfHeight = half, Feather = feather });
        }
    }

    private void ApplyPendingChanges()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_clearRequested)
        {
            _zones.Clear();
            _clearRequested = false;
            _removeRequested = -1;
        }
        else if (_removeRequested >= 0)
        {
            _zones.RemoveAt(_removeRequested);
            _removeRequested = -1;
        }

        if (_addRequested.HasValue)
        {
            _zones.Add(_addRequested.Value);
            _addRequested = null;
        }
    }

    private string NextZoneName()
    {
        while (true)
        {
            string candidate = $"зона {_nextIndex++}";
            bool exists = false;
            for (int index = 0; index < _zones.Count; index++)
            {
                if (_zones.Zones[index].Name == candidate)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                return candidate;
            }
        }
    }
}
