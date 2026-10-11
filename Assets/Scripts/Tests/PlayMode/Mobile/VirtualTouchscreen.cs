#nullable enable

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode.Mobile;

// Отдельный тачскрин теста. На устройстве один палец даёт два потока:
// Input System (EnhancedTouch — трекер жестов) и указатель UI Toolkit
// (кнопки, стик). В batch-прогоне UI Toolkit без фокуса приложения не
// читает устройства, поэтому второй поток подаётся явно: событие указателя
// с типом touch уходит в элемент, который панель находит под пальцем, —
// тем же разбором, что делает диспетчер. Координаты — экранные пиксели.
internal sealed class VirtualTouchscreen : IDisposable
{
    private readonly InputSettings.EditorInputBehaviorInPlayMode _previousBehavior;
    private readonly InputSettings.BackgroundBehavior _previousBackground;

    public VirtualTouchscreen()
    {
        // Редактор по умолчанию отдаёт указатели игре только при фокусе Game
        // View; в batch-прогоне окна нет, и касания молча отбрасывались бы.
        _previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Без фокуса приложения (batch) устройства, не умеющие работать в
        // фоне, выключаются — тачскрин в их числе.
        _previousBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Device = InputSystem.AddDevice<Touchscreen>("KernTestTouchscreen");
        InputSystem.EnableDevice(Device);
        Device.MakeCurrent();
    }

    public Touchscreen Device { get; }

    // Панель, в которую идёт второй поток; null — только Input System.
    public IPanel? Panel { get; set; }

    public float PanelScale { get; set; } = 1f;

    public void Begin(int touchId, Vector2 screenPosition)
    {
        Queue(touchId, UnityEngine.InputSystem.TouchPhase.Began, screenPosition);
        SendToPanel(touchId, screenPosition, PointerPhase.Down);
    }

    public void Move(int touchId, Vector2 screenPosition)
    {
        Queue(touchId, UnityEngine.InputSystem.TouchPhase.Moved, screenPosition);
        SendToPanel(touchId, screenPosition, PointerPhase.Move);
    }

    public void End(int touchId, Vector2 screenPosition)
    {
        Queue(touchId, UnityEngine.InputSystem.TouchPhase.Ended, screenPosition);
        SendToPanel(touchId, screenPosition, PointerPhase.Up);
    }

    private enum PointerPhase
    {
        Down,
        Move,
        Up,
    }

    private void SendToPanel(int touchId, Vector2 screenPosition, PointerPhase phase)
    {
        if (Panel == null)
        {
            return;
        }

        Vector2 panelPosition = new(screenPosition.x / PanelScale, (Screen.height - screenPosition.y) / PanelScale);
        var pointer = new TestTouchPointer(panelPosition, touchId)
        {
            pressedButtons = phase == PointerPhase.Up ? 0 : 1,
        };

        // Захваченный палец получает события в захватившем элементе, как
        // при диспетчеризации; иначе — в верхнем элементе под пальцем.
        int pointerId = PointerId.touchPointerIdBase + touchId;
        VisualElement? target = Panel.GetCapturingElement(pointerId) as VisualElement ?? Panel.Pick(panelPosition);
        if (target == null)
        {
            return;
        }

        EventBase evt = phase switch
        {
            PointerPhase.Down => PointerDownEvent.GetPooled(pointer),
            PointerPhase.Move => PointerMoveEvent.GetPooled(pointer),
            _ => PointerUpEvent.GetPooled(pointer),
        };
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    public void Dispose()
    {
        InputSystem.RemoveDevice(Device);
        InputSystem.settings.editorInputBehaviorInPlayMode = _previousBehavior;
        InputSystem.settings.backgroundBehavior = _previousBackground;
    }

    private void Queue(int touchId, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
    {
        InputSystem.QueueStateEvent(Device, new TouchState
        {
            touchId = touchId,
            phase = phase,
            position = position,
            pressure = 1f,
        });
    }
}
