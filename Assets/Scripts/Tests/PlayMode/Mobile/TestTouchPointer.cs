#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode.Mobile;

// Касание пальцем для событий UI Toolkit: позиция — в координатах панели,
// тип указателя — touch, идентификатор — первый палец. Из этого строятся
// PointerDown/PointerUp, которые Clickable кнопки принимает за нажатие.
internal sealed class TestTouchPointer : IPointerEvent
{
    public TestTouchPointer(Vector2 panelPosition, int fingerIndex = 0)
    {
        position = panelPosition;
        localPosition = panelPosition;
        pointerId = PointerId.touchPointerIdBase + fingerIndex;
    }

    public int pointerId { get; }

    public string pointerType => UnityEngine.UIElements.PointerType.touch;

    public bool isPrimary => pointerId == PointerId.touchPointerIdBase;

    public int button => 0;

    public int pressedButtons { get; set; } = 1;

    public Vector3 position { get; }

    public Vector3 localPosition { get; }

    public Vector3 deltaPosition => Vector3.zero;

    public float deltaTime => 0f;

    public int clickCount => 1;

    public float pressure => 1f;

    public float tangentialPressure => 0f;

    public float altitudeAngle => 0f;

    public float azimuthAngle => 0f;

    public float twist => 0f;

    public Vector2 tilt => Vector2.zero;

    public PenStatus penStatus => PenStatus.None;

    public Vector2 radius => Vector2.one;

    public Vector2 radiusVariance => Vector2.zero;

    public EventModifiers modifiers => EventModifiers.None;

    public bool shiftKey => false;

    public bool ctrlKey => false;

    public bool commandKey => false;

    public bool altKey => false;

    public bool actionKey => false;
}
