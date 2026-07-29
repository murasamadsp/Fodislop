#nullable enable

using UnityEngine;

namespace Fodinae.Scripts.Player.Interfaces
{
    public interface IPlayerInput
    {
        Vector2 MoveInput { get; }
        bool WantsToToggleAutoDig { get; }
        bool WantsToToggleAggression { get; }
        bool WantsToDig { get; }
        bool WantsToGeo { get; }
        bool WantsToHeal { get; }
        bool WantsToBuildCyan { get; }
        bool WantsToBuildGray { get; }
        bool WantsToBuildGreen { get; }
        bool WantsToBuildWhite { get; }
        bool IsShiftPressed { get; }
        bool IsCtrlPressed { get; }
        void SetMovementInput(Vector2 input);
    }
}
