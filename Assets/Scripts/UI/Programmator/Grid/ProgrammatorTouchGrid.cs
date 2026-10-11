#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Programmator;

// Сетка программатора под пальцем. 16×12 клеток по 44 dp не помещаются на
// экран телефона, поэтому сетка двигается пальцем внутри своего окна, а
// жесты разводятся так:
//   тап             — клетка (выбор / повторный тап — радиальное меню);
//   долгое нажатие  — радиальное меню (на мыши это ПКМ);
//   движение пальца дальше порога — сдвиг сетки, клетка не трогается.
// Мышь сюда не попадает: её события фильтруются по типу указателя, и
// десктопная сетка работает как раньше.
internal sealed class ProgrammatorTouchGrid
{
    private const float DragThreshold = 10f;
    private const long LongPressMilliseconds = 450;

    private readonly VisualElement _viewport;
    private readonly VisualElement _content;
    private readonly Func<VisualElement, Vector2Int?> _cellAt;
    private readonly Action<int, int> _tap;
    private readonly Action<int, int> _longPress;

    private int _pointerId = -1;
    private Vector2 _start;
    private Vector2 _offsetAtStart;
    private Vector2 _offset;
    private bool _dragging;
    private bool _longPressFired;
    private Vector2Int? _pressedCell;
    private IVisualElementScheduledItem? _longPressTimer;

    public ProgrammatorTouchGrid(
        VisualElement viewport,
        VisualElement content,
        Func<VisualElement, Vector2Int?> cellAt,
        Action<int, int> tap,
        Action<int, int> longPress)
    {
        _viewport = viewport;
        _content = content;
        _cellAt = cellAt;
        _tap = tap;
        _longPress = longPress;

        _viewport.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
        _viewport.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
        _viewport.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
        _viewport.RegisterCallback<PointerCancelEvent>(_ => Reset());
        _viewport.RegisterCallback<PointerCaptureOutEvent>(_ => Reset());

        // Окно сетки меняет размер (поворот, раскрытие раскладушки) — сдвиг
        // приводится к новым границам.
        _viewport.RegisterCallback<GeometryChangedEvent>(_ => ApplyOffset(_offset));
    }

    private static bool IsTouch(IPointerEvent evt) => evt.pointerType != UnityEngine.UIElements.PointerType.mouse;

    private void OnDown(PointerDownEvent evt)
    {
        if (!IsTouch(evt) || _pointerId >= 0)
        {
            return;
        }

        _pointerId = evt.pointerId;
        _start = evt.position;
        _offsetAtStart = _offset;
        _dragging = false;
        _longPressFired = false;
        _pressedCell = evt.target is VisualElement target ? _cellAt(target) : null;
        _viewport.CapturePointer(evt.pointerId);

        if (_pressedCell is Vector2Int cell)
        {
            _longPressTimer = _viewport.schedule.Execute(() =>
            {
                if (_pointerId >= 0 && !_dragging)
                {
                    _longPressFired = true;
                    _longPress(cell.x, cell.y);
                }
            }).StartingIn(LongPressMilliseconds);
        }

        // Мышиная логика клетки (выбор с Ctrl/Shift) не должна видеть палец.
        evt.StopPropagation();
    }

    private void OnMove(PointerMoveEvent evt)
    {
        if (evt.pointerId != _pointerId)
        {
            return;
        }

        Vector2 delta = (Vector2)evt.position - _start;
        if (!_dragging && delta.magnitude >= DragThreshold)
        {
            _dragging = true;
            _longPressTimer?.Pause();
        }

        if (_dragging)
        {
            ApplyOffset(_offsetAtStart + delta);
        }

        evt.StopPropagation();
    }

    private void OnUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _pointerId)
        {
            return;
        }

        if (!_dragging && !_longPressFired && _pressedCell is Vector2Int cell)
        {
            _tap(cell.x, cell.y);
        }

        _viewport.ReleasePointer(evt.pointerId);
        Reset();
        evt.StopPropagation();
    }

    private void Reset()
    {
        _longPressTimer?.Pause();
        _longPressTimer = null;
        _pointerId = -1;
        _dragging = false;
        _pressedCell = null;
    }

    // Сдвиг ограничен так, что край сетки не уходит внутрь окна; если сетка
    // меньше окна по оси, по этой оси она стоит на месте.
    private void ApplyOffset(Vector2 offset)
    {
        Rect viewport = _viewport.contentRect;
        Rect content = _content.layout;
        if (float.IsNaN(viewport.width) || float.IsNaN(content.width))
        {
            return;
        }

        float minX = Mathf.Min(0f, viewport.width - content.width);
        float minY = Mathf.Min(0f, viewport.height - content.height);
        _offset = new Vector2(Mathf.Clamp(offset.x, minX, 0f), Mathf.Clamp(offset.y, minY, 0f));
        _content.style.translate = new Translate(_offset.x, _offset.y);
    }
}
