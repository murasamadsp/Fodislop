#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Kern.Player.Input;

// Касания мира — по пальцам, а не через Pointer.current: на сенсорном
// экране Pointer.current — только первый палец, и тап по миру вторым
// пальцем, пока первый держит стик, терялся.
//
// Палец принадлежит интерфейсу, если в окне кадра его начала UI Toolkit
// получил касание рядом с ним (PointerDown на корне документа, фаза
// погружения — до кнопок). Такие пальцы не дают ни тапа по миру, ни пинча.
//
//   тап по миру — палец без интерфейса, короче TapMaxSeconds и не дальше
//                 TapMaxTravel пикселей; событие WorldTapped, экранные px;
//   пинч        — два пальца без интерфейса; ConsumePinchScale отдаёт
//                 накопленное отношение расстояний (>1 — пальцы разошлись).
public sealed class TouchGestureTracker : IStartable, ITickable, IDisposable
{
    private const float TapMaxSeconds = 0.35f;
    private const float TapMaxTravel = 24f;
    private const float UiMatchPanelDistance = 48f;

    private readonly UIDocument _document;
    private readonly Dictionary<int, FingerState> _fingers = new();
    private readonly List<UiTouch> _uiTouches = new();
    private readonly List<FingerState> _worldFingers = new(2);
    private VisualElement? _root;
    private float _pinchScale = 1f;
    private float _lastPinchDistance = -1f;

    [Inject]
    public TouchGestureTracker(UIDocument document)
    {
        _document = document;
    }

    public event Action<Vector2>? WorldTapped;

    // Два пальца на мире: тапы в это время не засчитываются.
    public bool IsPinching { get; private set; }

    public void Start()
    {
        EnhancedTouchSupport.Enable();
    }

    public float ConsumePinchScale()
    {
        float scale = _pinchScale;
        _pinchScale = 1f;
        return scale;
    }

    public void Tick()
    {
        EnsureRootSubscription();
        if (!EnhancedTouchSupport.enabled)
        {
            return;
        }

        int frame = Time.frameCount;
        List<FingerState> worldFingers = _worldFingers;
        worldFingers.Clear();
        foreach (EnhancedTouch touch in EnhancedTouch.activeTouches)
        {
            int id = touch.finger.index;
            if (touch.phase == TouchPhase.Began || !_fingers.TryGetValue(id, out FingerState state))
            {
                state = new FingerState(touch.screenPosition, Time.unscaledTime, frame);
            }

            state.Position = touch.screenPosition;
            state.Travel = Mathf.Max(state.Travel, Vector2.Distance(state.Start, touch.screenPosition));
            if (!state.OwnedByUi && MatchesUiTouch(state))
            {
                state.OwnedByUi = true;
            }

            if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
            {
                Finish(state, touch.phase == TouchPhase.Ended);
                _fingers.Remove(id);
                continue;
            }

            _fingers[id] = state;

            // Палец считается миром со второго кадра: PointerDown интерфейса
            // мог прийти кадром позже начала касания.
            if (!state.OwnedByUi && frame - state.BeganFrame >= 1)
            {
                worldFingers.Add(state);
            }
        }

        UpdatePinch(worldFingers);
        _uiTouches.RemoveAll(touch => frame - touch.Frame > 2);
    }

    public void Dispose()
    {
        _root?.UnregisterCallback<PointerDownEvent>(OnUiPointerDown, TrickleDown.TrickleDown);
        _root = null;
    }

    private void Finish(FingerState state, bool ended)
    {
        if (!ended || state.OwnedByUi || state.WasPinching)
        {
            return;
        }

        if (Time.unscaledTime - state.StartTime <= TapMaxSeconds && state.Travel <= TapMaxTravel)
        {
            WorldTapped?.Invoke(state.Position);
        }
    }

    private void UpdatePinch(List<FingerState> worldFingers)
    {
        if (worldFingers.Count != 2)
        {
            IsPinching = false;
            _lastPinchDistance = -1f;
            return;
        }

        IsPinching = true;
        foreach (FingerState finger in worldFingers)
        {
            finger.WasPinching = true;
        }

        float distance = Vector2.Distance(worldFingers[0].Position, worldFingers[1].Position);
        if (_lastPinchDistance > 1f && distance > 1f)
        {
            _pinchScale *= distance / _lastPinchDistance;
        }

        _lastPinchDistance = distance;
    }

    private bool MatchesUiTouch(FingerState state)
    {
        if (_uiTouches.Count == 0 || _root?.panel == null)
        {
            return false;
        }

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, state.Start);
        foreach (UiTouch touch in _uiTouches)
        {
            if (Mathf.Abs(touch.Frame - state.BeganFrame) <= 1 &&
                Vector2.Distance(touch.PanelPosition, panelPosition) <= UiMatchPanelDistance)
            {
                return true;
            }
        }

        return false;
    }

    // Корень документа пересоздаётся при перевключении UIDocument —
    // подписка держится за актуальный.
    private void EnsureRootSubscription()
    {
        VisualElement? root = _document != null && _document.isActiveAndEnabled ? _document.rootVisualElement : null;
        if (root == _root)
        {
            return;
        }

        _root?.UnregisterCallback<PointerDownEvent>(OnUiPointerDown, TrickleDown.TrickleDown);
        _root = root;
        _root?.RegisterCallback<PointerDownEvent>(OnUiPointerDown, TrickleDown.TrickleDown);
    }

    // Касание досталось интерфейсу, если его цель — контрол или что-то
    // нарисованное: невидимые слои на весь экран (подписи мира) не в счёт.
    private void OnUiPointerDown(PointerDownEvent evt)
    {
        if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse || !IsUiTarget(evt.target))
        {
            return;
        }

        _uiTouches.Add(new UiTouch(Time.frameCount, evt.position));
    }

    private bool IsUiTarget(IEventHandler? target)
    {
        if (target is not VisualElement element || element == _root || element is TemplateContainer)
        {
            return false;
        }

        if (element is Button)
        {
            return true;
        }

        IResolvedStyle style = element.resolvedStyle;
        return style.backgroundColor.a > 0f ||
               style.backgroundImage.texture != null ||
               style.backgroundImage.sprite != null ||
               (element is Image image && image.image != null) ||
               (element is TextElement text && !string.IsNullOrEmpty(text.text));
    }

    private sealed class FingerState
    {
        public FingerState(Vector2 start, float startTime, int beganFrame)
        {
            Start = start;
            Position = start;
            StartTime = startTime;
            BeganFrame = beganFrame;
        }

        public Vector2 Start { get; }

        public float StartTime { get; }

        public int BeganFrame { get; }

        public Vector2 Position { get; set; }

        public float Travel { get; set; }

        public bool OwnedByUi { get; set; }

        public bool WasPinching { get; set; }
    }

    private readonly struct UiTouch
    {
        public UiTouch(int frame, Vector2 panelPosition)
        {
            Frame = frame;
            PanelPosition = panelPosition;
        }

        public int Frame { get; }

        public Vector2 PanelPosition { get; }
    }
}
