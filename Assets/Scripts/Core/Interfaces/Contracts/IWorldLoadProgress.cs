#nullable enable

using System;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Monotonic world-load phases published from the Game scope and consumed
    /// by the MainMenu descent loader (a sibling scope that cannot resolve
    /// Game-scope managers). The loader renders believable, signal-driven
    /// progress instead of a static first phase.
    /// </summary>
    public enum WorldLoadPhase
    {
        /// <summary>Connection/auth handshake in flight.</summary>
        Handshake = 0,

        /// <summary>WorldInitPacket received; world manifest applied.</summary>
        WorldManifest = 1,

        /// <summary>Local player has an authoritative server position.</summary>
        SpawnSync = 2,

        /// <summary>Terrain mesh is built and ready for gameplay.</summary>
        TerrainMesh = 3,

        /// <summary>Surface/lighting initialized and asset queues drained.</summary>
        SurfaceAssets = 4,

        /// <summary>World fully loaded; gameplay UI authorized.</summary>
        Done = 5,
    }

    /// <summary>
    /// Bootstrap-scoped relay for world readiness phases. GameManager reports
    /// phase transitions as its readiness gate advances; UI reads the current
    /// phase for progress presentation. Lives on the application scope so the
    /// MainMenu loader keeps rendering while MainGame is still starting up.
    /// </summary>
    public interface IWorldLoadProgress
    {
        WorldLoadPhase CurrentPhase { get; }

        event Action<WorldLoadPhase>? PhaseChanged;

        void Report(WorldLoadPhase phase);
    }
}
