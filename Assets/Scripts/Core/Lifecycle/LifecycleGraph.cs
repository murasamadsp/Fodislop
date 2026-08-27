#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Core.Lifecycle;

public sealed class LifecycleGraph
{
    private readonly List<ILifecycleParticipant> _ordered;
    private bool _initialized;

    public LifecycleGraph(IEnumerable<ILifecycleParticipant> participants)
    {
        _ordered = Sort(participants);
    }

    public IReadOnlyList<ILifecycleParticipant> OrderedParticipants => _ordered;

    public async UniTask InitializeAndPrepareAsync(
        LifecycleContext context,
        CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            for (int index = 0; index < _ordered.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _ordered[index].InitializeAsync(context, cancellationToken);
            }

            _initialized = true;
        }

        for (int index = 0; index < _ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _ordered[index].PrepareAsync(context, cancellationToken);
        }
    }

    public async UniTask EnterAsync(CancellationToken cancellationToken)
    {
        for (int index = 0; index < _ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _ordered[index].EnterAsync(cancellationToken);
        }
    }

    public async UniTask ExitAsync(CancellationToken cancellationToken)
    {
        for (int index = _ordered.Count - 1; index >= 0; index--)
        {
            await _ordered[index].ExitAsync(cancellationToken);
        }
    }

    public async UniTask DisposeAsync()
    {
        for (int index = _ordered.Count - 1; index >= 0; index--)
        {
            await _ordered[index].DisposeAsync();
        }
    }

    private static List<ILifecycleParticipant> Sort(
        IEnumerable<ILifecycleParticipant> participants)
    {
        var byType = new Dictionary<Type, ILifecycleParticipant>();
        foreach (ILifecycleParticipant participant in participants)
        {
            Type type = participant.GetType();
            if (!byType.TryAdd(type, participant))
            {
                throw new InvalidOperationException(
                    $"Lifecycle participant '{type.FullName}' is registered more than once.");
            }
        }

        var result = new List<ILifecycleParticipant>(byType.Count);
        var states = new Dictionary<Type, VisitState>(byType.Count);
        var path = new List<Type>();
        var phaseOrdered = new List<ILifecycleParticipant>(byType.Values);
        phaseOrdered.Sort(static (left, right) =>
        {
            int phase = left.Phase.CompareTo(right.Phase);
            return phase != 0
                ? phase
                : string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
        });

        foreach (ILifecycleParticipant participant in phaseOrdered)
        {
            Visit(participant.GetType(), byType, states, path, result);
        }

        return result;
    }

    private static void Visit(
        Type type,
        IReadOnlyDictionary<Type, ILifecycleParticipant> byType,
        IDictionary<Type, VisitState> states,
        IList<Type> path,
        ICollection<ILifecycleParticipant> result)
    {
        if (states.TryGetValue(type, out VisitState state))
        {
            if (state == VisitState.Visited)
            {
                return;
            }

            int cycleStart = path.IndexOf(type);
            var cycle = new List<string>();
            for (int index = cycleStart; index < path.Count; index++)
            {
                cycle.Add(path[index].Name);
            }

            cycle.Add(type.Name);
            throw new InvalidOperationException(
                $"Lifecycle dependency cycle: {string.Join(" -> ", cycle)}.");
        }

        if (!byType.TryGetValue(type, out ILifecycleParticipant? participant))
        {
            throw new InvalidOperationException(
                $"Lifecycle dependency '{type.FullName}' is not registered.");
        }

        states[type] = VisitState.Visiting;
        path.Add(type);
        foreach (Type dependency in participant.Dependencies)
        {
            if (!byType.TryGetValue(dependency, out ILifecycleParticipant? dependencyParticipant))
            {
                throw new InvalidOperationException(
                    $"Lifecycle participant '{type.FullName}' requires missing " +
                    $"participant '{dependency.FullName}'.");
            }

            if (dependencyParticipant.Phase > participant.Phase)
            {
                throw new InvalidOperationException(
                    $"Lifecycle participant '{type.FullName}' in phase {participant.Phase} " +
                    $"cannot depend on later phase {dependencyParticipant.Phase} " +
                    $"('{dependency.FullName}').");
            }

            Visit(dependency, byType, states, path, result);
        }

        path.RemoveAt(path.Count - 1);
        states[type] = VisitState.Visited;
        result.Add(participant);
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
