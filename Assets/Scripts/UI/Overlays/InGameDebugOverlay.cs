#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering.PostProcessing;
using Fodinae.Tools;
using Fodinae.Tools.Imgui;
using Fodinae.Tools.Imgui.Windows;
using Fodinae.World;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>Owns the in-game IMGUI diagnostics host and its shortcuts.</summary>
    [DisallowMultipleComponent]
    public sealed class InGameDebugOverlay : MonoBehaviour
    {
        [Inject]
        private LightingEngine _lighting = null!;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        private readonly WorldGizmoOptions _gizmos = new();
        private readonly ToolWindow?[] _ownedWindows = new ToolWindow?[7];
        private RenderBypassWindow? _bypassWindow;
        private bool _registered;

        public bool IsEnabled
        {
            get => ToolWindows.Enabled;
            set
            {
                EnsureWindows();
                SetToolsEnabled(value);
                UpdateTelemetryState();
            }
        }

        private void OnEnable()
        {
            EnsureWindows();
            UpdateTelemetryState();
        }

        private void Start()
        {
            EnsureWindows();
        }

        private void OnDisable()
        {
            ToolWindows.SaveLayout(immediate: true);
            SetToolsEnabled(false);
            _telemetry?.SetAllocationTrackingEnabled(false);
        }

        private void OnDestroy()
        {
            ToolWindows.SaveLayout(immediate: true);
            SetToolsEnabled(false);
            _telemetry?.SetAllocationTrackingEnabled(false);
            foreach (ToolWindow? window in _ownedWindows)
            {
                if (window != null)
                {
                    ToolWindows.Unregister(window);
                    window.Dispose();
                }
            }

            _registered = false;
        }

        private void EnsureWindows()
        {
            if (_telemetry == null || _debugSettings == null)
            {
                return;
            }

            if (_registered)
            {
                foreach (ToolWindow? window in _ownedWindows)
                {
                    if (window != null && !ToolWindows.IsRegistered(window))
                    {
                        ToolWindows.Register(window);
                    }
                }

                return;
            }

            var toolbar = new ToolbarWindow();
            var stats = new FrameStatsWindow(_telemetry, _lighting);
            var world = new WorldInfoWindow(
                _telemetry,
                _lighting,
                _mapManager,
                _storage,
                _localPlayer,
                _gameplayCamera,
                _debugSettings,
                stats);
            var bypass = new RenderBypassWindow(_debugSettings, _lighting, _gizmos);
            var lightingCost = new LightingCostWindow(_lighting, _telemetry);
            var breakdown = new FrameBreakdownWindow();
            var packets = new PacketTrafficWindow();
            _bypassWindow = bypass;
            _ownedWindows[0] = toolbar;
            _ownedWindows[1] = stats;
            _ownedWindows[2] = world;
            _ownedWindows[3] = bypass;
            _ownedWindows[4] = lightingCost;
            _ownedWindows[5] = breakdown;
            _ownedWindows[6] = packets;

            foreach (ToolWindow? window in _ownedWindows)
            {
                if (window != null)
                {
                    ToolWindows.Register(window);
                }
            }

            _registered = true;
        }

        private void Update()
        {
            EnsureWindows();
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null &&
                !ToolWindows.HasKeyboardCapture &&
                keyboard.f1Key.wasPressedThisFrame)
            {
                SetToolsEnabled(!ToolWindows.Enabled);
                UpdateTelemetryState();
            }

            if (!ToolWindows.Enabled)
            {
                return;
            }

            ReleaseCaptureOnEscape(keyboard);
            UpdateTelemetryState();
            _telemetry.BeginFrame();
            ToolWindows.Tick();
        }

        /// <summary>
        /// Возвращает управление игре по Escape.
        /// </summary>
        /// <remarks>
        /// Поле ввода или ползунок в IMGUI удерживают клавиатуру, и пока захват
        /// не снят, игра не слышит ни одной клавиши. Выходом было закрыть весь
        /// интерфейс по F1 и потерять раскладку; теперь достаточно Escape.
        ///
        /// Переключения окон клавишами здесь нет намеренно. Цифровой ряд занят
        /// хотбаром, функциональный — рабочим местом колориста, и всякая
        /// раскладка поверх этого либо конфликтует с игрой, либо запоминается
        /// хуже, чем один щелчок в списке инструментов.
        /// </remarks>
        private static void ReleaseCaptureOnEscape(Keyboard? keyboard)
        {
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame &&
                ToolWindows.HasKeyboardCapture)
            {
                ToolWindows.ReleaseInputCapture();
            }
        }

        private void UpdateTelemetryState()
        {
            _telemetry?.SetAllocationTrackingEnabled(ToolWindows.AnySampling);
        }

        private static void SetToolsEnabled(bool enabled)
        {
            ToolWindows.Enabled = enabled;
            if (enabled)
            {
                return;
            }

            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareMode = CompareMode.Off;
            PostProcessRuntimeState.CompareBefore = false;
        }

        private void OnGUI()
        {
            if (_registered)
            {
                ToolWindows.Draw();
            }
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !ToolWindows.Enabled)
            {
                return;
            }

            if (!_gizmos.ShowGrid && !_gizmos.ShowCursor)
            {
                return;
            }

            DebugOverlayGizmos.DrawWorldDebugGizmos(
                _gizmos.ShowGrid,
                _gizmos.ShowCursor,
                _mapManager,
                _storage,
                _localPlayer,
                _gameplayCamera);
        }
    }
}
