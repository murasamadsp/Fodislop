#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Core.Lifecycle
{
    public abstract class LifecycleParticipant : MonoBehaviour, ILifecycleParticipant, ILifecycleState
    {
        public virtual LifecyclePhase Phase => LifecyclePhase.Gameplay;

        public virtual IReadOnlyList<Type> Dependencies => Array.Empty<Type>();

        public LifecycleState State { get; private set; } = LifecycleState.Created;

        public bool IsEntered => State == LifecycleState.Entered;

        public async UniTask InitializeAsync(
            LifecycleContext context,
            CancellationToken cancellationToken)
        {
            RequireState(LifecycleState.Created, nameof(InitializeAsync));
            State = LifecycleState.Initializing;
            await OnInitializeAsync(context, cancellationToken);
            State = LifecycleState.Initialized;
        }

        public async UniTask PrepareAsync(
            LifecycleContext context,
            CancellationToken cancellationToken)
        {
            RequireState(
                State is LifecycleState.Initialized or LifecycleState.Exited,
                nameof(PrepareAsync));
            State = LifecycleState.Preparing;
            await OnPrepareAsync(context, cancellationToken);
            State = LifecycleState.Prepared;
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken)
        {
            RequireState(LifecycleState.Prepared, nameof(EnterAsync));
            State = LifecycleState.Entering;
            await OnEnterAsync(cancellationToken);
            State = LifecycleState.Entered;
        }

        public async UniTask ExitAsync(CancellationToken cancellationToken)
        {
            if (State != LifecycleState.Entered)
            {
                return;
            }

            State = LifecycleState.Exiting;
            await OnExitAsync(cancellationToken);
            State = LifecycleState.Exited;
        }

        public async UniTask DisposeAsync()
        {
            if (State == LifecycleState.Disposed)
            {
                return;
            }

            State = LifecycleState.Disposing;
            await OnDisposeAsync();
            State = LifecycleState.Disposed;
        }

        protected virtual UniTask OnInitializeAsync(
            LifecycleContext context,
            CancellationToken cancellationToken) => UniTask.CompletedTask;

        protected virtual UniTask OnPrepareAsync(
            LifecycleContext context,
            CancellationToken cancellationToken) => UniTask.CompletedTask;

        protected virtual UniTask OnEnterAsync(CancellationToken cancellationToken) =>
            UniTask.CompletedTask;

        protected virtual UniTask OnExitAsync(CancellationToken cancellationToken) =>
            UniTask.CompletedTask;

        protected virtual UniTask OnDisposeAsync() => UniTask.CompletedTask;

        private void RequireState(LifecycleState required, string operation) =>
            RequireState(State == required, operation);

        private void RequireState(bool valid, string operation)
        {
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.{operation} is invalid while lifecycle state is {State}.");
            }
        }
    }
}
