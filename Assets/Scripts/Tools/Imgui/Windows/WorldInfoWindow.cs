#nullable enable

using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.World;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Tools.Imgui.Windows;

/// <summary>
/// Мир, игрок, железо, HDR: два текстовых блока отладки.
/// </summary>
/// <remarks>
/// Текст собирает прежний <see cref="DebugOverlayTextFormatter"/> — он писался
/// без единой аллокации в кадре и переписывать его незачем. Изменилось только
/// то, куда он попадает: раньше в два Label поверх игры с инлайновой
/// геометрией, теперь в перетаскиваемое окно, которое не спорит за место с
/// панелью игрока и хотбаром.
///
/// Обновление раз в секунду, как и было: колонки читают, а не смотрят, и
/// шестьдесят пересборок строки в секунду ради этого не нужны.
/// </remarks>
public sealed class WorldInfoWindow : ToolWindow
{
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingEngine? _lighting;
    private readonly MapManager? _mapManager;
    private readonly IWorldDataStorage? _storage;
    private readonly ILocalPlayerState? _localPlayer;
    private readonly IGameplayCamera? _camera;
    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly FrameStatsWindow _stats;
    private readonly StringBuilder _leftSb = new(1024);
    private readonly StringBuilder _rightSb = new(1024);
    private string _left = string.Empty;
    private string _right = string.Empty;
    private float _nextUpdate;
    private Vector2 _scroll;

    public WorldInfoWindow(
        IFrameTelemetry telemetry,
        LightingEngine? lighting,
        MapManager? mapManager,
        IWorldDataStorage? storage,
        ILocalPlayerState? localPlayer,
        IGameplayCamera? camera,
        IRuntimeDebugSettings debugSettings,
        FrameStatsWindow stats)
        : base("Мир и клиент", new Rect(628f, 16f, 520f, 560f))
    {
        _telemetry = telemetry;
        _lighting = lighting;
        _mapManager = mapManager;
        _storage = storage;
        _localPlayer = localPlayer;
        _camera = camera;
        _debugSettings = debugSettings;
        _stats = stats;
    }

    public override Vector2 MinimumSize => new(420f, 360f);

    public override void Tick()
    {
        if (!Visible || Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + 1f;

        DebugOverlayTextFormatter.FormatLeftColumn(
            _leftSb, _localPlayer?.Current, _mapManager, _storage, _camera);
        _left = _leftSb.ToString();

        DebugOverlayTextFormatter.FormatRightColumn(
            _rightSb, _telemetry, _lighting, _debugSettings, _camera, _stats.SolvesPerSecond);
        _right = _rightSb.ToString();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        _left = string.Empty;
        _right = string.Empty;
        _leftSb.Clear();
        _rightSb.Clear();
    }

    protected override void DrawContent()
    {
        using (var scroll = new GUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            GUILayout.Label("МИР И ИГРОК", SectionLabelStyle);
            using (new GUILayout.VerticalScope(CardStyle))
            {
                GUILayout.Label(_left, RichLabelStyle);
            }

            GUILayout.Label("РЕНДЕР И КЛИЕНТ", SectionLabelStyle);
            using (new GUILayout.VerticalScope(CardStyle))
            {
                GUILayout.Label(_right, RichLabelStyle);
            }
        }
    }
}
