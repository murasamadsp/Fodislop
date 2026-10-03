#nullable enable

namespace Kern.Core.Interfaces;

/// <summary>
///     Debug override for how fast the tail thinks its owner is travelling. The chain is fed
///     a root that drifts away from the robot at the mode's speed while the offset is
///     subtracted back out at write time, so the tail holds the length it would have at that
///     speed without ever detaching from the body.
/// </summary>
public enum TailMotionDebugMode
{
    /// <summary>No override: the tail follows the robot's real movement.</summary>
    Normal = 0,

    /// <summary>Half of RobotMoveSpeed, a clearly stretched tail.</summary>
    Minimal = 1,

    /// <summary>RobotMoveSpeed itself.</summary>
    Medium = 2,

    /// <summary>Four times RobotMoveSpeed, close to where the anti-stretch clamps bite.</summary>
    Maximum = 3,
}

public interface IRuntimeDebugSettings
{
    bool IgnoreCollision { get; set; }
    bool BypassLightingCompute { get; set; }
    bool BypassTerrainDraw { get; set; }
    bool BypassCpuMeshRebuild { get; set; }
    bool ShowRobotDebugVisuals { get; set; }
    bool BypassGameUI { get; set; }
    TailMotionDebugMode TailMotionDebugMode { get; set; }
}

public sealed class RuntimeDebugSettings : IRuntimeDebugSettings
{
    public bool IgnoreCollision { get; set; }
    public bool BypassLightingCompute { get; set; }
    public bool BypassTerrainDraw { get; set; }
    public bool BypassCpuMeshRebuild { get; set; }
    public bool ShowRobotDebugVisuals { get; set; }
    public bool BypassGameUI { get; set; }
    public TailMotionDebugMode TailMotionDebugMode { get; set; } = TailMotionDebugMode.Normal;
}
