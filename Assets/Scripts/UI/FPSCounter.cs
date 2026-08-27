#nullable enable

using Fodinae.Core;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Displays the current frames‑per‑second in the top‑center of the screen using UI Toolkit.
    /// Updates each frame and formats the value with one decimal place.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        private const int SAMPLE_SIZE = 30;
        private readonly float[] _frameTimes = new float[SAMPLE_SIZE];
        private int _frameIndex;
        private float _runningSum;

        [Inject]
        private UIDocument? _injectedDoc;
        [Inject]
        private LightingEngine _lightingEngine = null!;

        private UIDocument? _doc;
        private VisualElement? _rootElement;
        private Label? _fpsLabel;
        private int _pingMs;
        private int _onlinePlayers;
        private int _onlineProgrammator;
        private float _nextDisplayUpdate;
        private bool _showDetailedProfiler;
        private int _currentDebugViewIndex;

        public float CurrentFps { get; private set; }

        public int PingMs => _pingMs;

        public int OnlinePlayers => _onlinePlayers;

        public int OnlineProgrammator => _onlineProgrammator;

        protected void Start()
        {
            EnsureUI();
        }

        protected void OnEnable()
        {
            if (_fpsLabel == null)
            {
                EnsureUI();
            }
            else if (_rootElement != null)
            {
                _rootElement.style.display = DisplayStyle.Flex;
            }
        }

        protected void OnDisable()
        {
            if (_rootElement != null)
            {
                _rootElement.style.display = DisplayStyle.None;
            }
        }

        protected void OnDestroy()
        {
            _rootElement?.RemoveFromHierarchy();
            _rootElement = null;
            _fpsLabel = null;
        }

        private void EnsureUI()
        {
            if (_fpsLabel != null)
            {
                return;
            }

            // Никогда не резолвим из текущего контейнера здесь: во время
            // GameLifetimeScope.Configure он ещё указывает на родительский (Bootstrap)
            // скоуп, а AddComponent на только что созданном менеджере немедленно дёргает
            // OnEnable — Resolve<UIDocument> бросил бы VContainerException. [Inject]-поле
            // заполняется при завершении сборки scope; до этого момента EnsureUI
            // ретраится из Update каждый кадр.
            _doc = _injectedDoc;
            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }


            _rootElement = new VisualElement
            {
                name = "fps-counter-container",
                pickingMode = PickingMode.Ignore,
            };

            _rootElement.style.position = Position.Absolute;
            _rootElement.style.top = 8;
            _rootElement.style.left = new Length(50, LengthUnit.Percent);
            _rootElement.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _rootElement.style.alignItems = Align.Center;

            _fpsLabel = new Label
            {
                name = "fps-counter-label",
                pickingMode = PickingMode.Ignore,
            };

            _fpsLabel.style.color = Color.white;
            _fpsLabel.style.fontSize = 13;
            _fpsLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _fpsLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            _fpsLabel.style.borderTopLeftRadius = 4;
            _fpsLabel.style.borderTopRightRadius = 4;
            _fpsLabel.style.borderBottomLeftRadius = 4;
            _fpsLabel.style.borderBottomRightRadius = 4;
            _fpsLabel.style.paddingLeft = 8;
            _fpsLabel.style.paddingRight = 8;
            _fpsLabel.style.paddingTop = 2;
            _fpsLabel.style.paddingBottom = 2;

            _rootElement.Add(_fpsLabel);
            _doc.rootVisualElement.Add(_rootElement);
        }

        protected void Update()
        {
            FrameProfiler.BeginFrame();

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    _showDetailedProfiler = !_showDetailedProfiler;
                    FrameProfiler.SetAllocationTrackingEnabled(_showDetailedProfiler);
                }

                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    LightingEngine? engine = _lightingEngine;
                    if (engine != null)
                    {
                        _currentDebugViewIndex = (_currentDebugViewIndex + 1) % 6;
                        var view = (LightingEngine.DebugView)_currentDebugViewIndex;
                        engine.SetDebugView(view);
                    }
                }
            }

            _runningSum -= _frameTimes[_frameIndex];
            _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
            _runningSum += _frameTimes[_frameIndex];
            _frameIndex = (_frameIndex + 1) % SAMPLE_SIZE;
            float avg = _runningSum / SAMPLE_SIZE;
            float fps = avg > 0f ? 1f / avg : 0f;
            CurrentFps = fps;

            if (_fpsLabel == null)
            {
                EnsureUI();
            }

            if (_fpsLabel != null && Time.unscaledTime >= _nextDisplayUpdate)
            {
                _nextDisplayUpdate = Time.unscaledTime + 0.1f;
                float frameTimeMs = avg * 1000f;
                if (!_showDetailedProfiler)
                {
                    _fpsLabel.text = $"FPS: {fps:F0} ({frameTimeMs:F1}ms)  Ping: {_pingMs}ms  Online: {_onlinePlayers}  [F3 for details]";
                }
                else
                {
                    LightingEngine? engine = _lightingEngine;
                    string debugViewStr = engine != null ? engine.ActiveDebugView.ToString() : "Off";
                    float gcAllocKb = FrameProfiler.GcAllocPerFrameBytes / 1024f;

                    _fpsLabel.text =
                        $"FPS: {fps:F0} ({frameTimeMs:F1}ms)  Ping: {_pingMs}ms  Online: {_onlinePlayers}\n" +
                        $"<color=#aaffaa>[Terrain CPU]</color> Mesh: {FrameProfiler.TerrainMeshTimeMs:F2}ms | Flood: {FrameProfiler.TerrainFloodFillTimeMs:F2}ms | Cache: {FrameProfiler.TerrainCacheTimeMs:F2}ms | Upload: {FrameProfiler.TerrainGpuUploadTimeMs:F2}ms\n" +
                        $"<color=#ffffaa>[Lighting CPU]</color> Build: {FrameProfiler.LightingBuildCommandsTimeMs:F2}ms | Execute: {FrameProfiler.LightingExecuteCommandsTimeMs:F2}ms | Cmd: {FrameProfiler.LightingCommandBufferBytes / 1024f:F1}KB | View: {debugViewStr} [F2]\n" +
                        $"<color=#ffffaa>[Lighting counts]</color> Static: {FrameProfiler.LightingStaticSolveCount} | Dynamic: {FrameProfiler.LightingDynamicSolveCount} | Invalidations: {FrameProfiler.LightingRegionInvalidationCount} | DynLights: {FrameProfiler.ActiveDynamicLights}\n" +
                        $"<color=#ffaaff>[Memory]</color> GC: {gcAllocKb:F1} KB/f | " +
                        $"{FrameProfiler.GcAllocTotalPerSecondBytes / (1024f * 1024f):F2} MB/s | " +
                        $"collections: {FrameProfiler.GcCollectionCount} | [F3 to close]";
                }
            }
        }

        public void SetPing(int ms) => _pingMs = ms;

        public void SetOnline(int players, int programmator)
        {
            _onlinePlayers = players;
            _onlineProgrammator = programmator;
        }
    }
}
