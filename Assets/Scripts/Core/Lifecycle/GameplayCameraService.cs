#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core.Lifecycle;

/// <summary>
/// Default <see cref="IGameplayCamera"/>: exposes the persistent application
/// camera bound by <c>BootstrapLifetimeScope.BindApplicationCamera</c>. The
/// reference is constant for the application lifetime and is not expected to
/// be replaced, so the service returns the bound instance directly.
/// </summary>
public sealed class GameplayCameraService : IGameplayCamera
{
    public Camera Camera { get; }

    public GameplayCameraService(Camera camera)
    {
        Camera = camera ?? throw new System.ArgumentNullException(nameof(camera));
    }
}
