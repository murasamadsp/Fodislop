#nullable enable

using System;
using Fodinae.Core.Interfaces;

namespace Fodinae.Core.Lifecycle;

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
