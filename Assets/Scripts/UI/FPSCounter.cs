#nullable enable

using Fodinae.Core;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Fodinae.UI
{
    /// <summary>
    /// Displays the current frames‑per‑second in the top‑right corner of the screen.
    /// Attach this component to a GameObject that has a Canvas (or create a new Canvas
    /// automatically if none exists). The script creates a UI Text element, updates it
    /// each frame and formats the value with one decimal place.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        private const int SAMPLE_SIZE = 30;
        private readonly float[] _frameTimes = new float[SAMPLE_SIZE];
        private int _frameIndex;
        private float _runningSum;

        private Text? _fpsText;
        private Canvas? _ownedCanvas;
        private int _pingMs;
        private int _onlinePlayers;
        private int _onlineProgrammator;
        private float _nextDisplayUpdate;

        public float CurrentFps { get; private set; }
        public int PingMs => _pingMs;
        public int OnlinePlayers => _onlinePlayers;
        public int OnlineProgrammator => _onlineProgrammator;

        protected void Awake()
        {
            GameObject canvasGO = new GameObject("FPSCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
            canvasGO.AddComponent<CanvasScaler>();
            _ownedCanvas = canvas;

            GameObject textGO = new GameObject("FPSLabel");
            textGO.transform.SetParent(canvas.transform, false);
            _fpsText = textGO.AddComponent<Text>();

            _fpsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_fpsText.font == null)
            {
                _fpsText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }

            _fpsText.fontSize = 14;
            _fpsText.alignment = TextAnchor.UpperCenter;
            _fpsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _fpsText.color = Color.white;
            _fpsText.raycastTarget = false;

            RectTransform rt = _fpsText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -10);
        }

        protected void OnDestroy()
        {
            if (_ownedCanvas != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_ownedCanvas.gameObject);
                }
                else
                {
                    DestroyImmediate(_ownedCanvas.gameObject);
                }
            }
        }

        private bool _showDetailedProfiler;
        private int _currentDebugViewIndex;

        protected void Update()
        {
            FrameProfiler.BeginFrame();

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    _showDetailedProfiler = !_showDetailedProfiler;
                }

                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    var engine = TerrariaLightingEngine.Instance;
                    if (engine != null)
                    {
                        _currentDebugViewIndex = (_currentDebugViewIndex + 1) % 6;
                        var view = (TerrariaLightingEngine.DebugView)_currentDebugViewIndex;
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

            if (_fpsText != null && Time.unscaledTime >= _nextDisplayUpdate)
            {
                _nextDisplayUpdate = Time.unscaledTime + 0.1f;
                float frameTimeMs = avg * 1000f;
                if (!_showDetailedProfiler)
                {
                    _fpsText.text = $"FPS: {fps:F0} ({frameTimeMs:F1}ms)  Ping: {_pingMs}ms  Online: {_onlinePlayers}  [F3 for details]";
                }
                else
                {
                    var engine = TerrariaLightingEngine.Instance;
                    string debugViewStr = engine != null ? engine.ActiveDebugView.ToString() : "Off";
                    float gcAllocKb = FrameProfiler.GcAllocPerFrameBytes / 1024f;

                    _fpsText.text =
                        $"FPS: {fps:F0} ({frameTimeMs:F1}ms)  Ping: {_pingMs}ms  Online: {_onlinePlayers}\n" +
                        $"<color=#aaffaa>[Terrain CPU]</color> Mesh: {FrameProfiler.TerrainMeshTimeMs:F2}ms | Flood: {FrameProfiler.TerrainFloodFillTimeMs:F2}ms | Cache: {FrameProfiler.TerrainCacheTimeMs:F2}ms | Upload: {FrameProfiler.TerrainGpuUploadTimeMs:F2}ms\n" +
                        $"<color=#ffffaa>[Lighting GPU]</color> Solve: {FrameProfiler.LightingSolveTimeMs:F2}ms | DynLights: {FrameProfiler.ActiveDynamicLights} | View: {debugViewStr} [F2]\n" +
                        $"<color=#ffaaff>[Memory]</color> GC: {gcAllocKb:F1} KB/f | [F3 to close]";
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
