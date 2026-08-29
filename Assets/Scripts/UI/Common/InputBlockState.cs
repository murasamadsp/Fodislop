#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.UI.Programmator;

namespace Fodinae.UI;

public sealed class InputBlockState : IInputBlocker
{
    private readonly ServerWindowPresenter _windows;
    private readonly MapModeState _mapMode;

    public InputBlockState(ServerWindowPresenter windows, MapModeState mapMode)
    {
        _windows = windows;
        _mapMode = mapMode;
    }

    public bool IsInputBlocked =>
        ChatInput.IsFocused ||
        _windows.HasOpenWindows ||
        _windows.IsModalShowing ||
        PauseMenu.IsMenuOpen ||
        ProgrammatorGrid.IsOpen ||
        _mapMode.IsOpen;

    public string? TopWindowTag => _windows.TopWindowTag;
}
