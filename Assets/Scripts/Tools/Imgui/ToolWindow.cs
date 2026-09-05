#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Tools.Imgui;

/// <summary>
/// Окно инструмента: перетаскиваемое, с собственной видимостью.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОБЩИЙ ТИП. До этого каждый отладочный вид жил сам по себе: колонки диагностики
/// собирались из VisualElement с инлайновыми стилями, графики рисовались через
/// generateVisualContent, счётчик кадров держал свой Label, а рабочее место
/// колориста — свои GUI.Window. Четыре способа показать число на экране, четыре
/// места, где заводится клавиша, и ни одного общего представления о том, какие
/// инструменты вообще есть.
///
/// Теперь способ один. Окно объявляет заголовок и содержимое, всё остальное —
/// перетаскивание, видимость, порядок, клавиши — делает <see cref="ToolWindows"/>.
///
/// ПОЧЕМУ ОКНА ПУБЛИЧНЫ. Система живёт в сборке <c>Fodinae.Runtime</c>
/// (`Assets/Scripts/` целиком), а её хозяин — в <c>Fodinae.UI</c>: он обязан
/// быть MonoBehaviour, уже стоящим на сцене, а такой нашёлся только там.
/// Через границу сборок <c>internal</c> не виден, поэтому типы окон публичны
/// не по небрежности, а потому что это межсборочный API.
/// </remarks>
public abstract class ToolWindow : IDisposable
{
    private readonly Rect _initialRect;
    private bool _initialStateCaptured;
    private bool _initialVisible;
    private bool _visible;
    private string? _drawError;
    private string? _pendingDrawError;
    private bool _retryRequested;
    private Vector2? _pendingSize;

    protected ToolWindow(string title, Rect initialRect)
    {
        Title = title;
        Rect = initialRect;
        _initialRect = initialRect;
    }

    public string Title { get; }

    public Rect Rect;

    /// <summary>Видимость окна. Мастер-тумблер системы её не стирает.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible != value)
            {
                _visible = value;
                OnVisibilityChanged(value);
            }
        }
    }

    /// <summary>
    /// Номер окна для IMGUI. Раздаётся реестром при регистрации: совпадение
    /// номеров склеивает окна в одно, и найти такое по виду почти невозможно.
    /// </summary>
    internal int Id { get; set; }

    /// <summary>
    /// Нужен ли окну сбор данных. Отделено от видимости, потому что часть
    /// инструментов обязана копить историю и в закрытом виде — иначе график
    /// после открытия десять секунд пустой.
    /// </summary>
    public virtual bool WantsSampling => Visible;

    /// <summary>Smallest usable content area before screen bounds take priority.</summary>
    public virtual Vector2 MinimumSize => new(240f, 150f);

    /// <summary>The toolbar is the recovery path for every other window.</summary>
    protected virtual bool CanClose => true;

    /// <summary>Whether the bottom-right resize grip is available.</summary>
    protected virtual bool CanResize => true;

    protected static GUIStyle SectionLabelStyle => ToolTheme.SectionLabel;

    protected static GUIStyle RichLabelStyle => ToolTheme.RichLabel;

    protected static GUIStyle WrappedLabelStyle => ToolTheme.WrappedLabel;

    protected static GUIStyle MutedLabelStyle => ToolTheme.MutedLabel;

    protected static GUIStyle MetricLabelStyle => ToolTheme.MetricLabel;

    protected static GUIStyle ActiveButtonStyle => ToolTheme.ActiveButton;

    protected static GUIStyle SecondaryButtonStyle => ToolTheme.SecondaryButton;

    protected static GUIStyle DangerButtonStyle => ToolTheme.DangerButton;

    protected static GUIStyle SegmentedButtonStyle => ToolTheme.SegmentedButton;

    protected static GUIStyle CardStyle => ToolTheme.Card;

    /// <summary>Кадровая логика. Зовётся всегда, даже когда окно закрыто.</summary>
    public virtual void Tick()
    {
    }

    public void ResetPosition()
    {
        Rect = _initialRect;
    }

    internal void CaptureInitialState()
    {
        if (_initialStateCaptured)
        {
            return;
        }

        _initialStateCaptured = true;
        _initialVisible = Visible;
    }

    internal void ResetForPlaySession()
    {
        Rect = _initialRect;
        Visible = _initialVisible;
        _drawError = null;
        _pendingDrawError = null;
        _retryRequested = false;
        _pendingSize = null;
        OnPlaySessionReset();
    }

    protected virtual void OnPlaySessionReset()
    {
    }

    protected virtual void OnVisibilityChanged(bool visible)
    {
    }

    public void Dispose()
    {
        OnDispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void OnDispose()
    {
    }

    protected abstract void DrawContent();

    internal Rect ApplyPendingSize(Rect drawnRect)
    {
        if (!_pendingSize.HasValue)
        {
            return drawnRect;
        }

        drawnRect.size = _pendingSize.Value;
        _pendingSize = null;
        return drawnRect;
    }

    internal void DrawWindow(int id)
    {
        if (_retryRequested && Event.current.type == EventType.Layout)
        {
            _retryRequested = false;
            _drawError = null;
            _pendingDrawError = null;
        }

        if (_pendingDrawError != null && Event.current.type == EventType.Layout)
        {
            _drawError = _pendingDrawError;
            _pendingDrawError = null;
        }

        if (Event.current.type == EventType.MouseDown &&
            new Rect(0f, 0f, Rect.width, Rect.height).Contains(Event.current.mousePosition))
        {
            GUI.BringWindowToFront(id);
            ToolWindows.NotifyWindowFocused(this);
        }

        ToolTheme.DrawHeaderRule(Rect.width);
        if (CanClose && GUI.Button(
                new Rect(Rect.width - 30f, 4f, 24f, 22f),
                "×",
                ToolTheme.CloseButton))
        {
            ToolWindows.RequestVisibility(this, visible: false);
        }

        if (_drawError != null)
        {
            GUILayout.Space(2f);
            GUILayout.Label(
                "Окно не смогло отрисоваться. Остальные инструменты продолжают работать.",
                ToolTheme.ErrorLabel);
            GUILayout.TextArea(_drawError, GUILayout.MinHeight(60f));
            if (GUILayout.Button("Повторить", ActiveButtonStyle))
            {
                _retryRequested = true;
            }

            DrawResizeGrip();
            GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
            return;
        }

        Color previousColor = GUI.color;
        Color previousBackgroundColor = GUI.backgroundColor;
        Color previousContentColor = GUI.contentColor;
        bool previousEnabled = GUI.enabled;
        int previousDepth = GUI.depth;
        Matrix4x4 previousMatrix = GUI.matrix;
        try
        {
            DrawContent();
        }
        catch (ExitGUIException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (_pendingDrawError == null)
            {
                _pendingDrawError = $"{exception.GetType().Name}: {exception.Message}";
                Debug.LogException(exception);
            }

            // После исключения GUILayout-кэш текущего события уже неполон.
            // Продолжать Repaint с ним нельзя: ошибка одного окна породит
            // вторичную ArgumentException про несовпавшее число контролов и
            // визуально уронит весь реестр. ExitGUI отдаёт Unity управление и
            // следующий Layout строит безопасный экран ошибки с нуля.
            GUIUtility.ExitGUI();
        }
        finally
        {
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackgroundColor;
            GUI.contentColor = previousContentColor;
            GUI.enabled = previousEnabled;
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
        }

        DrawResizeGrip();

        // Ручка — только полоса заголовка. Перетаскивание за содержимое
        // означало бы, что окно уезжает при каждом промахе мимо ползунка.
        GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
    }

    private void DrawResizeGrip()
    {
        if (!CanResize)
        {
            return;
        }

        const float gripSize = 18f;
        Rect grip = new(Rect.width - gripSize, Rect.height - gripSize, gripSize, gripSize);
        int controlId = GUIUtility.GetControlID(Id ^ 0x5E51, FocusType.Passive);
        Event currentEvent = Event.current;
        switch (currentEvent.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (currentEvent.button == 0 && grip.Contains(currentEvent.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    Vector2 size = _pendingSize ?? Rect.size;
                    size.x = Mathf.Max(MinimumSize.x, size.x + currentEvent.delta.x);
                    size.y = Mathf.Max(MinimumSize.y, size.y + currentEvent.delta.y);
                    _pendingSize = size;
                    currentEvent.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                }

                break;

            case EventType.Repaint:
                DrawResizeGlyph(grip, GUIUtility.hotControl == controlId);
                break;

            default:
                break;
        }
    }

    private static void DrawResizeGlyph(Rect grip, bool active)
    {
        Color previousColor = GUI.color;
        GUI.color = active ? ToolTheme.Accent : new Color(0.46f, 0.54f, 0.61f, 0.85f);
        Texture2D pixel = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(grip.xMax - 5f, grip.yMax - 4f, 3f, 2f), pixel);
        GUI.DrawTexture(new Rect(grip.xMax - 8f, grip.yMax - 7f, 3f, 2f), pixel);
        GUI.DrawTexture(new Rect(grip.xMax - 11f, grip.yMax - 10f, 3f, 2f), pixel);
        GUI.color = previousColor;
    }
}
