#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Lifecycle;
using NUnit.Framework;

namespace Fodinae.Tests.Core;

public sealed class LifecycleGraphTests
{
    [Test]
    public void OrdersDependenciesBeforeDependants()
    {
        var dependency = new DependencyParticipant();
        var dependant = new DependantParticipant();

        var graph = new LifecycleGraph([dependant, dependency]);

        Assert.That(graph.OrderedParticipants, Is.EqualTo(new ILifecycleParticipant[]
        {
            dependency,
            dependant,
        }));
    }

    [Test]
    public void RejectsMissingDependency()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => _ = new LifecycleGraph([new DependantParticipant()]))!;

        StringAssert.Contains(nameof(DependencyParticipant), exception.Message);
    }

    private abstract class StubParticipant : ILifecycleParticipant
    {
        public virtual LifecyclePhase Phase => LifecyclePhase.Gameplay;

        public virtual IReadOnlyList<Type> Dependencies => Array.Empty<Type>();

        public UniTask InitializeAsync(
            LifecycleContext context,
            CancellationToken cancellationToken) => UniTask.CompletedTask;

        public UniTask PrepareAsync(
            LifecycleContext context,
            CancellationToken cancellationToken) => UniTask.CompletedTask;

        public UniTask EnterAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

        public UniTask ExitAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

        public UniTask DisposeAsync() => UniTask.CompletedTask;
    }

    private sealed class DependencyParticipant : StubParticipant
    {
    }

    private sealed class DependantParticipant : StubParticipant
    {
        public override IReadOnlyList<Type> Dependencies => [typeof(DependencyParticipant)];
    }
}
