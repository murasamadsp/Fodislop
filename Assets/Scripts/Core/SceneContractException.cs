#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>
/// Thrown when an authored scene does not satisfy its composition-root
/// contract: a required serialized reference is missing, a reference belongs
/// to another scene, a required root or manager is absent, or a root is
/// authored in the wrong activation state.
///
/// Startup boundaries catch this type specifically to report a single
/// diagnostic and cancel the transition instead of half-starting the scene.
/// </summary>
public sealed class SceneContractException : InvalidOperationException
{
    public SceneContractException(string message)
        : base(message)
    {
    }

    public SceneContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
