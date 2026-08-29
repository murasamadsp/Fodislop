#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.UI.Programmator;

namespace Fodinae.UI;

public sealed class InputBlockState : IInputBlocker
{
    private readonly ServerWindowPresenter _windows;

    public InputBlockState(ServerWindowPresenter windows)
    {
        _windows = windows;
    }

    public bool IsInputBlocked =>
        ChatInput.IsFocused ||
        _windows.HasOpenWindows ||
        _windows.IsModalShowing ||
        PauseMenu.IsMenuOpen ||
        ProgrammatorGrid.IsOpen;

    public string? TopWindowTag => _windows.TopWindowTag;
}
