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
    /// <summary>Высота свёрнутого окна: только полоса заголовка.</summary>
    public const float CollapsedHeight = ToolTheme.HeaderHeight + 4f;

    private readonly Rect _initialRect;
    private bool _initialStateCaptured;
    private bool _initialVisible;
    private bool _visible;
    private string? _drawError;
    private string? _pendingDrawError;
    private bool _retryRequested;
    private Vector2? _pendingSize;
    private bool _collapsed;
    private float _expandedHeight;

    protected ToolWindow(string title, Rect initialRect)
    {
        Title = title;
        DisplayTitle = title.ToUpperInvariant();
        Rect = initialRect;
        _initialRect = initialRect;
    }

    public string Title { get; }

    /// <summary>
    /// Заголовок в полосе окна.
    /// </summary>
    /// <remarks>
    /// Верхний регистр посчитан один раз в конструкторе, а не при каждой
    /// отрисовке: IMGUI рисует по несколько событий на кадр, и строка,
    /// собираемая в <c>OnGUI</c>, — это мусор в куче на ровном месте, который
    /// к тому же виден в том самом окне статистики, что стоит рядом.
    /// </remarks>
    public string DisplayTitle { get; }

    public Rect Rect;

    /// <summary>
    /// Свёрнуто ли окно в одну полосу заголовка.
    /// </summary>
    /// <remarks>
    /// Не то же самое, что закрытое. Закрытое окно исчезает из виду целиком, и
    /// чтобы понять, что оно вообще есть, надо идти в список инструментов.
    /// Свёрнутое остаётся на своём месте и помнит размер: его открывают
    /// обратно одним щелчком там же, где свернули. С пятью окнами на экране это
    /// разница между «убрал с глаз» и «потерял».
    /// </remarks>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            if (value)
            {
                _expandedHeight = Rect.height;
            }

            _collapsed = value;
            _pendingSize = new Vector2(
                Rect.width,
                value ? CollapsedHeight : Mathf.Max(MinimumSize.y, _expandedHeight));
            ToolWindows.NotifyLayoutChanged();
        }
    }

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

    /// <summary>
    /// Можно ли восстанавливать сохранённую видимость этого окна.
    /// </summary>
    /// <remarks>
    /// Совпадает с возможностью закрыть окно, и не случайно. Список
    /// инструментов закрыть нельзя — он и есть путь ко всем остальным, — а
    /// значит сохранённое «скрыт» вернуло бы состояние, из которого нет выхода
    /// ничем, кроме стирания настроек вручную.
    /// </remarks>
    public bool CanRestoreVisibility => CanClose;

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
        _collapsed = false;
        _expandedHeight = 0f;
        _pendingSize = null;
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
        _collapsed = false;
        _expandedHeight = 0f;
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

        bool focused = ToolWindows.IsFocused(this);
        var local = new Rect(0f, 0f, Rect.width, Rect.height);
        ToolChrome.DrawHeaderMarker(ToolTheme.HeaderHeight, focused);
        ToolChrome.DrawHeaderRule(Rect.width, ToolTheme.HeaderHeight, focused);
        ToolChrome.DrawCornerBrackets(local, focused);

        // Кнопка закрытия отодвинута от правого края на ширину среза: на самом
        // углу рамки её нет, и кнопка висела бы в пустоте.
        float closeX = Rect.width - 32f;
        float collapseX = CanClose ? closeX - 26f : closeX;
        GUI.Label(
            new Rect(13f, 0f, Mathf.Max(0f, collapseX - 19f), ToolTheme.HeaderHeight),
            DisplayTitle,
            ToolTheme.WindowTitle);
        if (CanClose && GUI.Button(
                new Rect(closeX, 5f, 24f, 20f),
                "×",
                ToolTheme.CloseButton))
        {
            ToolWindows.RequestVisibility(this, visible: false);
        }

        if (GUI.Button(
                new Rect(collapseX, 5f, 24f, 20f),
                Collapsed ? "+" : "−",
                ToolTheme.CloseButton))
        {
            Collapsed = !Collapsed;
        }

        if (Collapsed)
        {
            // Свёрнутое окно не рисует содержимое и не растягивается, но ручку
            // перетаскивания сохраняет: полоса заголовка — это всё, что от
            // него осталось, и она обязана остаться подвижной.
            GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
            return;
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
        Texture2D pixel = ToolPalette.White;

        // Три диагональных штриха, а не сплошной уголок: уголок здесь уже есть —
        // его рисует обвязка, — и второй такой же читался бы как сбой рамки.
        for (int i = 0; i < 3; i++)
        {
            float offset = 4f + i * 3f;
            GUI.color = active
                ? ToolPalette.Accent
                : ToolPalette.Fade(ToolPalette.Accent, 0.70f - i * 0.18f);
            GUI.DrawTexture(new Rect(grip.xMax - offset - 1f, grip.yMax - offset, 3f, 2f), pixel);
        }

        GUI.color = previousColor;
    }
}
