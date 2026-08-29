#nullable enable

using System;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Interactive In-Game Debug Overlay (toggled with F3).
    /// Provides real-time HUD metrics and runtime world gizmos (chunks, entities, cursor inspector).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InGameDebugOverlay : MonoBehaviour
    {
        [Inject]
        private Fodinae.World.Lighting.LightingEngine _lighting = null!;
        [Inject]
        private Fodinae.Rendering.PostProcessing.PostProcessController _postProcess = null!;

        [Header("Visualization Channels")]
        [SerializeField]
        private bool _showGrid = true;
        [SerializeField]
        private bool _showEntities = true;
        [SerializeField]
        private bool _showCursor = true;

        private bool _isEnabled;
        private GUIStyle? _boxStyle;
        private GUIStyle? _textStyle;
        private readonly StringBuilder _sb = new(512);

        private float _fpsTimer;
        private int _fpsFrames;
        private float _currentFps;
        private float _currentFrameMs;

        private ulong _lastSolveCount;
        private float _solvesPerSecond;
        private readonly System.Collections.Generic.List<Fodinae.World.Lighting.LightingEngine.CascadeCostSample> _cascadeCosts = new(4);

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private RobotManager _robotManager = null!;
        [Inject]
        private BuildingManager _buildingManager = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        private void Awake()
        {
            useGUILayout = false;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                _isEnabled = !_isEnabled;
            }

            if (!_isEnabled)
            {
                return;
            }

            // Subkey toggles when overlay is active
            if (Keyboard.current != null)
            {
                var kb = Keyboard.current;
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
                {
                    _showGrid = !_showGrid;
                }

                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
                {
                    _showEntities = !_showEntities;
                }

                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
                {
                    _showCursor = !_showCursor;
                }

                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame || kb.f4Key.wasPressedThisFrame)
                {
                    Fodinae.World.Lighting.LightingEngine.BypassLightingCompute = !Fodinae.World.Lighting.LightingEngine.BypassLightingCompute;
                }

                if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame || kb.f5Key.wasPressedThisFrame)
                {
                    Fodinae.Rendering.PostProcessing.PostProcessRendererFeature.BypassPostProcessPass = !Fodinae.Rendering.PostProcessing.PostProcessRendererFeature.BypassPostProcessPass;
                }

                if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame || kb.f6Key.wasPressedThisFrame)
                {
                    Fodinae.World.Terrain.TerrainRenderer.BypassTerrainDraw = !Fodinae.World.Terrain.TerrainRenderer.BypassTerrainDraw;
                }

                if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame || kb.f7Key.wasPressedThisFrame)
                {
                    Fodinae.World.Terrain.TerrainRenderer.BypassCpuMeshRebuild = !Fodinae.World.Terrain.TerrainRenderer.BypassCpuMeshRebuild;
                }

                if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame || kb.f8Key.wasPressedThisFrame)
                {
                    if (_lighting != null)
                    {
                        float current = _lighting.DynamicLightIntensity;
                        _lighting.SetDynamicLightSettings(current > 0.01f ? 0f : 1.25f, _lighting.DynamicLightColor);
                    }
                }
            }

            UpdateFps();
        }

        /// <summary>
        /// Draws the world debug gizmos in the only context that can render them.
        /// </summary>
        /// <remarks>
        /// These helpers are built on UnityEditor.Handles, which needs the drawing context
        /// Unity sets up around the gizmo pass. Called from Update there is no such context
        /// and Handles.BeginLineDrawing dereferences null — an exception, with a full stack
        /// trace captured, on every single frame. That is not just noise: capturing a managed
        /// stack trace 60+ times a second is expensive enough to show up as lost frames,
        /// which is what made the overlay look like a rendering regression.
        /// </remarks>
        private void OnDrawGizmos()
        {
            if (!_isEnabled || !Application.isPlaying)
            {
                return;
            }

            DrawWorldDebugGizmos();
        }

        private void UpdateFps()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.25f)
            {
                _currentFps = _fpsFrames / _fpsTimer;
                _currentFrameMs = (_fpsTimer / _fpsFrames) * 1000f;

                // Solves per second, not per frame: the engine skips solves on
                // its own cadence, so "how expensive is one solve" only means
                // something next to "how often does one happen".
                ulong solveCount = _lighting?.SolveCount ?? _lastSolveCount;
                _solvesPerSecond = (solveCount - _lastSolveCount) / _fpsTimer;
                _lastSolveCount = solveCount;

                _fpsFrames = 0;
                _fpsTimer = 0f;
            }
        }

        private void EnsureDependencies()
        {
            if (_mapManager == null || _storage == null || _robotManager == null || _buildingManager == null)
            {
                throw new InvalidOperationException(
                    "[InGameDebugOverlay] MainGame dependencies were not injected.");
            }
        }

        private void DrawWorldDebugGizmos()
        {
            EnsureDependencies();
            ILocalPlayer? player = _localPlayer.Current;

            if (_showGrid && _mapManager != null && _mapManager.IsWorldInitialized && player != null)
            {
                DrawChunkGrid(player.Position, _mapManager.WorldHeight);
            }

            if (_showCursor && _mapManager != null && _mapManager.IsWorldInitialized)
            {
                DrawCursorHighlight(_mapManager.WorldHeight);
            }
        }

        private void DrawChunkGrid(Vector2Int playerServerPos, int worldHeight)
        {
            const int chunkSize = 32;
            int playerChunkX = playerServerPos.x / chunkSize;
            int playerChunkY = playerServerPos.y / chunkSize;

            // Draw 3x3 chunks around the player
            for (int cx = playerChunkX - 1; cx <= playerChunkX + 1; cx++)
            {
                for (int cy = playerChunkY - 1; cy <= playerChunkY + 1; cy++)
                {
                    if (cx < 0 || cy < 0)
                    {
                        continue;
                    }

                    int serverLeft = cx * chunkSize;
                    int serverTop = cy * chunkSize;
                    Vector3 origin = CoordinateUtils.ServerToUnityPos(serverLeft, serverTop, worldHeight);
                    Vector3 center = origin + new Vector3(chunkSize * 0.5f - 0.5f, -(chunkSize * 0.5f - 0.5f), 0f);

                    FodinaeGizmos.DrawBounds(center, new Vector2(chunkSize, chunkSize), new Color(0f, 0.8f, 1f, 0.4f));
                }
            }
        }

        private void DrawCursorHighlight(int worldHeight)
        {
            Camera? cam = _gameplayCamera?.Camera;
            if (cam == null || Mouse.current == null)
            {
                return;
            }

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
            if (worldPos.y < 0f || worldPos.y >= worldHeight || worldPos.x < 0f)
            {
                return;
            }

            Vector2Int serverCell = CoordinateUtils.UnityToServerPos(worldPos, worldHeight);
            Vector3 cellCenter = CoordinateUtils.ServerToUnityPos(serverCell.x, serverCell.y, worldHeight);

            bool passable = false;
            if (_storage is MapStorage mapStorage && mapStorage.CellLayer != null && _mapManager != null)
            {
                CellType type = mapStorage.CellLayer.GetCellSync(serverCell.x, serverCell.y);
                var config = _mapManager.GetCellConfig(type);
                passable = type == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
            }

            Color highlightColor = passable ? Color.green : Color.red;
            FodinaeGizmos.DrawBounds(cellCenter, Vector2.one * 0.95f, highlightColor);
        }

        private void OnGUI()
        {
            if (!_isEnabled)
            {
                return;
            }

            EnsureDependencies();
            InitStyles();

            ILocalPlayer? player = _localPlayer.Current;

            _sb.Clear();
            _sb.AppendLine("<b>[F3] FODINAE IN-GAME DEBUG</b>");
            _sb.AppendLine($"FPS: {_currentFps:F0} ({_currentFrameMs:F1} ms) | GC: {Profiler.GetMonoUsedSizeLong() / (1024 * 1024)} MB");

            if (player != null && player.HasServerPosition)
            {
                _sb.AppendLine($"Player Server: ({player.Position.x}, {player.Position.y}) | Dir: {player.LastDirection}");
                _sb.AppendLine($"Player Unity: {player.transform.position:F2}");
            }
            else
            {
                _sb.AppendLine("Player: Not Spawned");
            }

            if (_mapManager != null && _mapManager.IsWorldInitialized)
            {
                _sb.AppendLine($"World: {_mapManager.WorldWidth}x{_mapManager.WorldHeight} ({_mapManager.WorldCodeName})");
            }

            // Hovered cell info
            Camera? cam = _gameplayCamera?.Camera;
            if (cam != null && Mouse.current != null && _mapManager != null && _mapManager.IsWorldInitialized)
            {
                Vector2 mouseScreen = Mouse.current.position.ReadValue();
                Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
                if (worldPos.y >= 0f && worldPos.y < _mapManager.WorldHeight && worldPos.x >= 0f)
                {
                    Vector2Int cell = CoordinateUtils.UnityToServerPos(worldPos, _mapManager.WorldHeight);

                    if (_storage is MapStorage mapStorage && mapStorage.CellLayer != null)
                    {
                        CellType cellType = mapStorage.CellLayer.GetCellSync(cell.x, cell.y);
                        var config = _mapManager.GetCellConfig(cellType);
                        bool passable = cellType == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
                        _sb.AppendLine($"Cursor Cell: ({cell.x}, {cell.y}) | Type: {cellType} ({(byte)cellType}) | Passable: {passable}");
                    }
                }
            }

            var lighting = _lighting;
            var ppController = _postProcess;

            _sb.AppendLine("---");
            string lightPassState = !Fodinae.World.Lighting.LightingEngine.BypassLightingCompute ? "<color=#00FF00>ON</color>" : "<color=#FF4444>MUTE</color>";
            string ppPassState = !Fodinae.Rendering.PostProcessing.PostProcessRendererFeature.BypassPostProcessPass ? "<color=#00FF00>ON</color>" : "<color=#FF4444>MUTE</color>";
            string terrainDrawState = !Fodinae.World.Terrain.TerrainRenderer.BypassTerrainDraw ? "<color=#00FF00>ON</color>" : "<color=#FF4444>MUTE</color>";
            string cpuMeshState = !Fodinae.World.Terrain.TerrainRenderer.BypassCpuMeshRebuild ? "<color=#00FF00>ON</color>" : "<color=#FF4444>MUTE</color>";

            string dynLightState = lighting != null && lighting.DynamicLightIntensity > 0.01f ? "<color=#00FF00>ON</color>" : "<color=#FF4444>MUTE</color>";

            _sb.AppendLine("<b>[Pipeline Macro Passes]</b>");
            _sb.AppendLine($"[4/F4] GPU Light (RC): {lightPassState} | [5/F5] GPU PostFX: {ppPassState}");
            _sb.AppendLine($"[6/F6] GPU Terrain:   {terrainDrawState} | [7/F7] CPU Meshing: {cpuMeshState}");
            _sb.AppendLine("<b>[Lighting (Pure 1-Pass Radiance Cascades)]</b>");
            _sb.AppendLine($"[8/F8] DynLights: {dynLightState}");
            _sb.AppendLine($"CPU Meshing: {FrameProfiler.TerrainMeshTimeMs:F2}ms | FloodFill: {FrameProfiler.TerrainFloodFillTimeMs:F2}ms");
            // TerrainRebuildCount/TerrainFullPopulateCount were already tracked
            // by TerrainRenderer but never surfaced anywhere - the one signal
            // that answers "is the Parallel.For terrain rebuild path firing
            // every frame or only on real region changes" sat uncollected.
            // Full > Rebuild would mean canScrollCache is false almost every
            // time, i.e. every rebuild pays for TerrainCellCache.PopulateFull +
            // TerrainPrecalculator.PrecalculateFull + TerrainMeshBuilder.BuildFull
            // - three Parallel.For passes instead of the cheap scroll/incremental
            // ones.
            _sb.AppendLine(
                $"Terrain Rebuilds: {FrameProfiler.TerrainRebuildCount} | Full Populate: {FrameProfiler.TerrainFullPopulateCount} | Dirty Patches: {FrameProfiler.TerrainDirtyPatchCount}");
            _sb.AppendLine(
                $"Lighting CPU: Build {FrameProfiler.LightingBuildCommandsTimeMs:F2}ms | Execute {FrameProfiler.LightingExecuteCommandsTimeMs:F2}ms | Cmd {FrameProfiler.LightingCommandBufferBytes / 1024f:F1}KB");
            _sb.AppendLine(
                $"Lighting counts: Static {FrameProfiler.LightingStaticSolveCount} | Dynamic {FrameProfiler.LightingDynamicSolveCount} | Invalidations {FrameProfiler.LightingRegionInvalidationCount}");
            AppendLightingCost(lighting);

            const float boxWidth = 560f;
            const float boxHeight = 360f;
            Rect rect = new Rect(10f, 10f, boxWidth, boxHeight);

            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), _sb.ToString(), _textStyle);
        }

        /// <summary>
        /// Prints what one radiance-cascade solve actually costs, so the price of
        /// a graphics setting can be read off the screen instead of inferred.
        /// </summary>
        private void AppendLightingCost(Fodinae.World.Lighting.LightingEngine? lighting)
        {
            if (lighting == null || !lighting.IsInitialized || lighting.CascadeCount == 0)
            {
                return;
            }

            lighting.CollectCascadeCosts(_cascadeCosts);
            long rays = 0;
            long raySteps = 0;
            long mergeTaps = 0;
            for (int index = 0; index < _cascadeCosts.Count; index++)
            {
                rays += _cascadeCosts[index].RayCount;
                raySteps += _cascadeCosts[index].RayStepCount;
                mergeTaps += _cascadeCosts[index].MergeTapCount;
            }

            long budget = lighting.CascadeAtlasBudgetEntries;

            // Flagged, because a fitted-down field means pixels-per-cell is no
            // longer what sets the resolution — the atlas limit is.
            string limited = lighting.CascadeBudgetLimited
                ? " <color=#FF4444>ATLAS-CAPPED</color>"
                : string.Empty;
            _sb.AppendLine(
                $"Light field: {lighting.FieldWidth}x{lighting.FieldHeight} | " +
                $"px/cell {lighting.EffectivePixelsPerCell:F2} of {lighting.RequestedPixelsPerCell:F0} req{limited}");
            _sb.AppendLine(
                $"Atlas: {lighting.AtlasEntryCount / 1e6f:F2}M of {budget / 1e6f:F2}M entries | " +
                $"cascades {lighting.CascadeCount} | max steps {lighting.MaximumIntervalSteps}");
            _sb.AppendLine(
                $"<b>Per solve: {rays / 1e6f:F2}M rays, {raySteps / 1e6f:F1}M ray-steps, " +
                $"{mergeTaps / 1e6f:F1}M atlas taps</b>");
            _sb.AppendLine(
                $"Solves/s: {_solvesPerSecond:F1} | dyn lights {lighting.UploadedDynamicLightCount}");

            for (int index = 0; index < _cascadeCosts.Count; index++)
            {
                var cost = _cascadeCosts[index];
                float share = raySteps > 0 ? cost.RayStepCount * 100f / raySteps : 0f;
                _sb.AppendLine(
                    $"  c{cost.Index}: {cost.ProbeWidth}x{cost.ProbeHeight} x{cost.DirectionCount}dir " +
                    $"[{cost.IntervalStart:F0}..{cost.IntervalEnd:F0}px] x{cost.StepCount}steps " +
                    $"= {cost.RayStepCount / 1e6f:F1}M ({share:F0}%)");
            }
        }

        private void InitStyles()
        {
            if (_boxStyle == null)
            {
                Texture2D bgTex = RuntimeTextureFactory.CreateRgba32NoMip(
                    1,
                    1,
                    "InGameDebugOverlayBackground",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                bgTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.85f));
                bgTex.Apply();

                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    normal = { background = bgTex },
                };
            }

            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = true,
                    normal = { textColor = new Color(0.9f, 0.95f, 1f, 1f) },
                };
            }
        }
    }
}
