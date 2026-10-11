#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.UI;

public sealed class MapPinchGestureTests
{
    [Test]
    public void SpreadingTwoFingersTwice_ZoomsInByTheWheelEquivalent()
    {
        var gesture = new MapPinchGesture();
        var image = new VisualElement();

        Assert.That(Down(gesture, image, 1, new Vector2(100, 100)), Is.False, "One finger is a drag, not a pinch.");
        Assert.That(Down(gesture, image, 2, new Vector2(200, 100)), Is.True, "The second finger must start a pinch.");

        float total = 0f;
        for (int step = 1; step <= 20; step++)
        {
            float offset = 50f * step / 20f;
            Move(gesture, 1, new Vector2(100 - offset, 100), ref total);
            Move(gesture, 2, new Vector2(200 + offset, 100), ref total);
        }

        // Расстояние выросло со 100 до 200: приближение на ln 2 / ln(1/0.85)
        // щелчков колеса; знак как у WheelEvent.delta.y (минус — приближение).
        float expected = -Mathf.Log(2f) / Mathf.Log(1f / 0.85f);
        Assert.That(total, Is.EqualTo(expected).Within(0.05f));
    }

    [Test]
    public void MouseNeverPinches()
    {
        var gesture = new MapPinchGesture();
        var image = new VisualElement();

        Assert.That(Down(gesture, image, 0, Vector2.zero, mouse: true), Is.False);
        Assert.That(Down(gesture, image, 0, Vector2.one, mouse: true), Is.False);
        Assert.That(gesture.IsPinching, Is.False);
    }

    private static bool Down(MapPinchGesture gesture, VisualElement image, int finger, Vector2 position, bool mouse = false)
    {
        using PointerDownEvent evt = PointerDownEvent.GetPooled(new FakePointer(finger, position, mouse));
        return gesture.Down(evt, image);
    }

    private static void Move(MapPinchGesture gesture, int finger, Vector2 position, ref float total)
    {
        using PointerMoveEvent evt = PointerMoveEvent.GetPooled(new FakePointer(finger, position, mouse: false));
        if (gesture.Move(evt, out float delta, out _))
        {
            total += delta;
        }
    }

    private sealed class FakePointer : IPointerEvent
    {
        public FakePointer(int finger, Vector2 position, bool mouse)
        {
            pointerId = mouse ? PointerId.mousePointerId : PointerId.touchPointerIdBase + finger;
            pointerType = mouse ? UnityEngine.UIElements.PointerType.mouse : UnityEngine.UIElements.PointerType.touch;
            this.position = position;
            localPosition = position;
        }

        public int pointerId { get; }

        public string pointerType { get; }

        public bool isPrimary => true;

        public int button => 0;

        public int pressedButtons => 1;

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
}
