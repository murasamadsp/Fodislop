#nullable enable

using UnityEngine;

namespace Kern.Player.Interfaces;
public interface IPlayerInput
{
    Vector2 MoveInput { get; }
    bool WantsToToggleAutoDig { get; }
    bool WantsToDig { get; }
    bool WantsToGeo { get; }
    bool WantsToHeal { get; }

    bool IsHealHeld { get; }
    bool WantsToBuildCyan { get; }
    bool WantsToBuildGray { get; }
    bool WantsToBuildGreen { get; }
    bool WantsToBuildWhite { get; }
    bool IsShiftPressed { get; }
    bool IsCtrlPressed { get; }
    bool IsGamepadActive { get; }
    void SetMovementInput(Vector2 input);
    void SetTouchMovementInput(Vector2 input);
    void SetTouchDigHeld(bool held);
    void SetTouchHealHeld(bool held);
    bool IsTouchFastMove { get; }
    void SetTouchFastMove(bool enabled);
    void QueueTouchAction(TouchPlayerAction action);
    void ClearQueuedTouchActions();
}

public enum TouchPlayerAction
{
    ToggleAutoDig,
    Geo,
    Heal,
    BuildCyan,
    BuildGray,
    BuildGreen,
    BuildWhite,
}
