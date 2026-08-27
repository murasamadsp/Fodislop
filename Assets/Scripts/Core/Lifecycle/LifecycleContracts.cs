#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer;

namespace Fodinae.Core.Lifecycle;

public enum LifecyclePhase
{
    Infrastructure = 0,
    Data = 100,
    World = 200,
    Gameplay = 300,
    Presentation = 400,
}

public enum LifecycleState
{
    Created,
    Initializing,
    Initialized,
    Preparing,
    Prepared,
    Entering,
    Entered,
    Exiting,
    Exited,
    Disposing,
    Disposed,
    Failed,
}

public readonly record struct LifecycleContext(
    Scene Scene,
    ulong Generation,
    IObjectResolver Services);

public interface ILifecycleParticipant
{
    LifecyclePhase Phase { get; }

    IReadOnlyList<Type> Dependencies { get; }

    UniTask InitializeAsync(LifecycleContext context, CancellationToken cancellationToken);

    UniTask PrepareAsync(LifecycleContext context, CancellationToken cancellationToken);

    UniTask EnterAsync(CancellationToken cancellationToken);

    UniTask ExitAsync(CancellationToken cancellationToken);

    UniTask DisposeAsync();
}

public interface ILifecycleState
{
    LifecycleState State { get; }

    bool IsEntered { get; }
}

public interface ISceneReadiness
{
    UniTask WaitUntilReadyAsync(CancellationToken cancellationToken);
}
