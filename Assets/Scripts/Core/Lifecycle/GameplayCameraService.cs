#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core.Lifecycle;

public sealed class GameplayCameraService : IGameplayCamera
{
    public Camera Camera { get; }

    public GameplayCameraService(Camera camera)
    {
        Camera = camera ?? throw new System.ArgumentNullException(nameof(camera));
    }
}
