#nullable enable

using System.Text;
using Fodinae.Networking;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>Displays FPS, ping and online count through the authored HUD.</summary>
    public class FPSCounter : MonoBehaviour
    {
        private const int SampleSize = 30;

        private readonly float[] _frameTimes = new float[SampleSize];
        private readonly StringBuilder _displayBuilder = new(128);
        private int _frameIndex;
        private float _runningSum;
        private float _nextDisplayUpdate;
        private Label? _fpsLabel;

        [Inject]
        private UIDocument _document = null!;
        [Inject]
        private NetworkStatusModel _networkStatus = null!;

        public float CurrentFps { get; private set; }

        public int PingMs => _networkStatus.PingMs;

        public int OnlinePlayers => _networkStatus.OnlinePlayers;

        public int OnlineProgrammator => _networkStatus.OnlineProgrammator;

        protected void Awake()
        {
            float initialDelta = Time.unscaledDeltaTime > 0f
                ? Time.unscaledDeltaTime
                : 1f / 60f;
            for (int index = 0; index < SampleSize; index++)
            {
                _frameTimes[index] = initialDelta;
            }

            _runningSum = initialDelta * SampleSize;
            CurrentFps = 1f / initialDelta;
        }

        protected void Start()
        {
            FindLabel();
        }

        protected void OnEnable()
        {
            FindLabel();
            if (_fpsLabel != null)
            {
                UIState.Show(_fpsLabel);
            }
        }

        protected void OnDisable()
        {
            if (_fpsLabel != null)
            {
                UIState.Hide(_fpsLabel);
            }
        }

        protected void OnDestroy()
        {
            _fpsLabel = null;
        }

        protected void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f)
            {
                delta = 0f;
            }

            _runningSum -= _frameTimes[_frameIndex];
            _frameTimes[_frameIndex] = delta;
            _runningSum += delta;
            _frameIndex = (_frameIndex + 1) % SampleSize;

            float averageDelta = _runningSum / SampleSize;
            CurrentFps = averageDelta > 0f ? 1f / averageDelta : 0f;

            if (_fpsLabel == null)
            {
                FindLabel();
            }

            if (_fpsLabel == null || Time.unscaledTime < _nextDisplayUpdate)
            {
                return;
            }

            _nextDisplayUpdate = Time.unscaledTime + 0.25f;
            _displayBuilder.Clear();
            _displayBuilder.Append("FPS: ").Append((int)CurrentFps)
                .Append(" (").Append((averageDelta * 1000f).ToString("F1"))
                .Append("ms)  Ping: ").Append(_networkStatus.PingMs)
                .Append("ms  Robots: ").Append(_networkStatus.OnlinePlayers)
                .Append("  Programmator: ").Append(_networkStatus.OnlineProgrammator)
                .Append("  [F1]");

            string text = _displayBuilder.ToString();
            if (_fpsLabel.text != text)
            {
                _fpsLabel.text = text;
            }
        }

        private void FindLabel()
        {
            if (_fpsLabel != null || _document == null)
            {
                return;
            }

            VisualElement? root = _document.rootVisualElement;
            _fpsLabel = root?.Q<Label>("FPSCounterLabel");
        }
    }
}
