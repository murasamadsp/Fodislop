#nullable enable

using System;
using Fodinae.Core.Interfaces;

namespace Fodinae.Core.Lifecycle;

/// <summary>
/// Default <see cref="IWorldLoadProgress"/>: idempotent phase transitions with
/// change notification. Registered on the Bootstrap scope so the MainMenu
/// loader (a sibling of the Game scope) can render progress while MainGame
/// starts up.
/// </summary>
public sealed class WorldLoadProgress : IWorldLoadProgress
{
    public WorldLoadPhase CurrentPhase { get; private set; }

    public event Action<WorldLoadPhase>? PhaseChanged;

    public void Report(WorldLoadPhase phase)
    {
        if (CurrentPhase == phase)
        {
            return;
        }

        CurrentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
