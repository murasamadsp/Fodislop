#nullable enable

using System;

namespace Fodinae.UI;

/// <summary>Scene-local state shared by the map views without a view-to-view dependency.</summary>
public sealed class MapModeState
{
    public bool IsOpen { get; private set; }

    public event Action<bool>? Changed;

    public void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        Changed?.Invoke(open);
    }
}
