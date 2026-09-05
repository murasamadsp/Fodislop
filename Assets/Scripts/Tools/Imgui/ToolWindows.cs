#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Tools.Imgui;

/// <summary>
/// Реестр окон инструментов и единственная точка их отрисовки.
/// </summary>
/// <remarks>
/// Статический реестр, а не поле хозяина: окна заводят разные подсистемы —
/// рендер, освещение, телеметрия, — и каждая из них знает про своё окно, но не
/// должна знать про хозяина. Хозяин, наоборот, не должен знать ни про одну из
/// них: он только рисует то, что зарегистрировано.
///
/// Это не точка доступа к логике: наружу видны список окон и мастер-тумблер.
/// Данные через реестр не ходят.
/// </remarks>
public static class ToolWindows
{
    private const int FirstWindowId = 0x7700;
    private const float ScreenMargin = 8f;

    private static readonly List<ToolWindow> Windows = [];
    private static readonly Dictionary<ToolWindow, bool> PendingVisibility = [];
    private static int _nextId = FirstWindowId;
    private static bool _enabled;
    private static bool _keyboardCaptured;
    private static bool _pointerCaptured;
    private static bool _layoutResetRequested;
    private static bool _releaseCaptureRequested;
    private static ToolWindow? _focusedWindow;
    private static ToolWindow? _pendingFocus;

    public static int SessionGeneration { get; private set; }

    /// <summary>Масштаб интерфейса инструментов под экраны Retina / High-DPI.</summary>
    public static float Scale => UIScaleUtility.IsRetinaOrHighDpi ? UIScaleUtility.RetinaDefaultScale : 1f;

    /// <summary>Мастер-тумблер: выключает всю систему разом.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                ReleaseInputCapture();
            }
        }
    }

    public static IReadOnlyList<ToolWindow> All => Windows;

    /// <summary>True while an IMGUI control owns keyboard focus.</summary>
    public static bool HasKeyboardCapture => Enabled && _keyboardCaptured;

    /// <summary>True while an IMGUI button, slider or drag owns the pointer.</summary>
    public static bool HasPointerCapture => Enabled && _pointerCaptured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        foreach (ToolWindow window in Windows)
        {
            window.ResetForPlaySession();
            window.Dispose();
        }

        Windows.Clear();
        PendingVisibility.Clear();
        ToolTheme.Reset();
        _nextId = FirstWindowId;
        _enabled = false;
        _keyboardCaptured = false;
        _pointerCaptured = false;
        _layoutResetRequested = false;
        _releaseCaptureRequested = true;
        _focusedWindow = null;
        _pendingFocus = null;
        SessionGeneration = unchecked(SessionGeneration + 1);
    }

    public static void Register(ToolWindow window)
    {
        if (Windows.Contains(window))
        {
            return;
        }

        window.CaptureInitialState();
        window.Id = _nextId++;
        Windows.Add(window);
    }

    public static bool IsRegistered(ToolWindow window) => Windows.Contains(window);

    public static void Unregister(ToolWindow window)
    {
        PendingVisibility.Remove(window);
        if (ReferenceEquals(_focusedWindow, window))
        {
            _focusedWindow = null;
        }

        if (ReferenceEquals(_pendingFocus, window))
        {
            _pendingFocus = null;
        }

        if (Windows.Remove(window))
        {
            // The removed window may own a text field or slider hot control.
            // Keeping that invisible control alive blocks gameplay input.
            ReleaseInputCapture();
        }
    }

    /// <summary>
    /// Queues an OnGUI-driven visibility change for the next Layout event.
    /// This keeps the control tree identical for the current event cycle.
    /// </summary>
    public static void RequestVisibility(ToolWindow window, bool visible)
    {
        PendingVisibility[window] = visible;
        if (!visible)
        {
            if (ReferenceEquals(_focusedWindow, window))
            {
                _focusedWindow = null;
            }

            ReleaseInputCapture();
        }
    }

    public static void RequestFocus(ToolWindow window)
    {
        if (Windows.Contains(window))
        {
            _pendingFocus = window;
        }
    }

    public static bool IsFocused(ToolWindow window) =>
        Enabled && ReferenceEquals(_focusedWindow, window);

    internal static void NotifyWindowFocused(ToolWindow window)
    {
        _focusedWindow = window;
        GUI.FocusWindow(window.Id);
    }

    public static void ResetLayout()
    {
        _layoutResetRequested = true;
    }

    public static void ReleaseInputCapture()
    {
        _keyboardCaptured = false;
        _pointerCaptured = false;
        _releaseCaptureRequested = true;
    }

    /// <summary>Tests an Input System point against visible IMGUI windows.</summary>
    public static bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!Enabled)
        {
            return false;
        }

        float scale = Scale;
        Vector2 guiPoint = new(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
        foreach (ToolWindow window in Windows)
        {
            if (window.Visible && window.Rect.Contains(guiPoint))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Есть ли открытое окно, которому нужен сбор данных.</summary>
    public static bool AnySampling
    {
        get
        {
            if (!Enabled)
            {
                return false;
            }

            foreach (ToolWindow window in Windows)
            {
                if (window.WantsSampling)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static void Tick()
    {
        // Кадровая логика идёт и при выключенной системе: инструмент, который
        // начинает копить историю только после открытия, показывает пустой
        // график ровно тогда, когда на него смотрят.
        foreach (ToolWindow window in Windows)
        {
            window.Tick();
        }
    }

    public static void Draw()
    {
        float scale = Scale;
        if (Event.current.type == EventType.Layout && _layoutResetRequested)
        {
            _layoutResetRequested = false;
            foreach (ToolWindow window in Windows)
            {
                window.ResetPosition();
                window.Rect = ConstrainToScreen(window, window.Rect, scale);
            }
        }

        if (Event.current.type == EventType.Layout && PendingVisibility.Count > 0)
        {
            foreach ((ToolWindow window, bool visible) in PendingVisibility)
            {
                if (Windows.Contains(window))
                {
                    window.Visible = visible;
                }
            }

            PendingVisibility.Clear();
        }

        if (_releaseCaptureRequested)
        {
            _releaseCaptureRequested = false;
            GUI.FocusControl(null);
            GUIUtility.hotControl = 0;
        }

        if (!Enabled)
        {
            _keyboardCaptured = false;
            _pointerCaptured = false;
            return;
        }

        Matrix4x4 previousMatrix = GUI.matrix;
        GUISkin previousSkin = GUI.skin;
        bool scaled = Mathf.Abs(scale - 1f) > 0.001f;
        GUI.skin = ToolTheme.ResolveSkin(previousSkin);
        if (scaled)
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        }

        try
        {
            foreach (ToolWindow window in Windows)
            {
                if (!window.Visible)
                {
                    continue;
                }

                Rect drawnRect = GUI.Window(
                    window.Id,
                    window.Rect,
                    window.DrawWindow,
                    window.Title);
                drawnRect = window.ApplyPendingSize(drawnRect);
                if (!IsFinite(drawnRect))
                {
                    window.ResetPosition();
                    drawnRect = window.Rect;
                }

                window.Rect = ConstrainToScreen(window, drawnRect, scale);
            }

            if (Event.current.type == EventType.Layout && _pendingFocus != null)
            {
                if (_pendingFocus.Visible && Windows.Contains(_pendingFocus))
                {
                    _focusedWindow = _pendingFocus;
                    GUI.FocusWindow(_pendingFocus.Id);
                }

                _pendingFocus = null;
            }
        }
        finally
        {
            GUI.matrix = previousMatrix;
            GUI.skin = previousSkin;
        }

        _keyboardCaptured = GUIUtility.keyboardControl != 0;
        _pointerCaptured = GUIUtility.hotControl != 0;
    }

    /// <summary>
    /// Не даёт окну уехать за край экрана.
    /// </summary>
    /// <remarks>
    /// Нужно по двум причинам. Начальные места окон подобраны под большой
    /// экран, и на меньшем часть из них открылась бы вне видимой области — то
    /// есть инструмент существовал бы, но добраться до него было бы нечем.
    /// И перетащить окно за край можно вручную, а вернуть уже нет: ручка — это
    /// полоса заголовка, а её там больше не будет.
    ///
    /// Окно остаётся целиком доступным; намеренно спрятать его можно через
    /// toolbar, а потерять заголовок за краем — уже не получится.
    /// </remarks>
    private static Rect ConstrainToScreen(ToolWindow window, Rect rect, float scale = 1f)
    {
        float availableWidth = Mathf.Max(1f, (Screen.width / scale) - ScreenMargin * 2f);
        float availableHeight = Mathf.Max(1f, (Screen.height / scale) - ScreenMargin * 2f);
        float minimumWidth = Mathf.Min(window.MinimumSize.x, availableWidth);
        float minimumHeight = Mathf.Min(window.MinimumSize.y, availableHeight);
        rect.width = Mathf.Clamp(rect.width, minimumWidth, availableWidth);
        rect.height = Mathf.Clamp(rect.height, minimumHeight, availableHeight);
        rect.x = Mathf.Clamp(rect.x, ScreenMargin, Mathf.Max(ScreenMargin, (Screen.width / scale) - rect.width - ScreenMargin));
        rect.y = Mathf.Clamp(rect.y, ScreenMargin, Mathf.Max(ScreenMargin, (Screen.height / scale) - rect.height - ScreenMargin));
        return rect;
    }

    private static bool IsFinite(Rect rect) =>
        IsFinite(rect.x) &&
        IsFinite(rect.y) &&
        IsFinite(rect.width) &&
        IsFinite(rect.height);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
