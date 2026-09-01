#nullable enable

using UnityEngine;

namespace Fodinae.Core.Interfaces;

/// <summary>
/// The single gameplay camera as a DI service.
///
/// DI-injected components that need the gameplay camera reference this interface
/// instead of calling <c>GameplayCamera.Resolve()</c>. The static holder exists
/// only for the URP <c>ScriptableRendererFeature</c>, which cannot receive field
/// injection; every VContainer-managed component reads the camera through this
/// typed dependency.
/// </summary>
public interface IGameplayCamera
{
    /// <summary>The persistent gameplay (application) camera.</summary>
    Camera Camera { get; }
}
