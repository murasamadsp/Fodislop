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
        private readonly ToolWindow?[] _ownedWindows = new ToolWindow?[4];
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
            SetToolsEnabled(false);
            _telemetry?.SetAllocationTrackingEnabled(false);
        }

        private void OnDestroy()
        {
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
            _bypassWindow = bypass;
            _ownedWindows[0] = toolbar;
            _ownedWindows[1] = stats;
            _ownedWindows[2] = world;
            _ownedWindows[3] = bypass;

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

            UpdateTelemetryState();
            _telemetry.BeginFrame();
            ToolWindows.Tick();
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
