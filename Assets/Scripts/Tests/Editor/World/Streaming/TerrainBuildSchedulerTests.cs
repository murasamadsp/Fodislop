#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World.Streaming;

[TestFixture]
public sealed class TerrainBuildSchedulerTests
{
    private sealed class BuildRequest
    {
        public BuildRequest(int value) => Value = value;

        public int Value { get; }
    }

    [Test]
    public void BuildRunsOffTheCallingThreadAndIsPolledWithoutWaiting()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        int callingThread = Environment.CurrentManagedThreadId;
        int buildThread = callingThread;
        using var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (request, cancellationToken) =>
            {
                buildThread = Environment.CurrentManagedThreadId;
                entered.Set();
                release.Wait(cancellationToken);
                return request.Value;
            });

        scheduler.Start(new BuildRequest(5));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);

        // Опрос не ждёт: пока сборка держится, результата просто нет.
        Stopwatch poll = Stopwatch.StartNew();
        Assert.That(scheduler.TryTakeCompleted(out _), Is.False);
        Assert.That(poll.ElapsedMilliseconds, Is.LessThan(50));
        Assert.That(scheduler.IsBusy, Is.True);
        Assert.That(scheduler.ActiveRequest!.Value, Is.EqualTo(5));

        release.Set();
        TerrainBuildCompletion<BuildRequest, int> completion = TakeCompletion(scheduler);
        Assert.That(completion.Result, Is.EqualTo(5));
        Assert.That(completion.WasCanceled, Is.False);
        Assert.That(completion.Failure, Is.Null);
        Assert.That(buildThread, Is.Not.EqualTo(callingThread));
        Assert.That(scheduler.IsBusy, Is.False);
    }

    [Test]
    public void SecondBuildCannotStartBeforeTheFirstIsTaken()
    {
        using var release = new ManualResetEventSlim();
        using var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (request, cancellationToken) =>
            {
                release.Wait(cancellationToken);
                return request.Value;
            });

        scheduler.Start(new BuildRequest(1));
        Assert.Throws<InvalidOperationException>(() => scheduler.Start(new BuildRequest(2)));

        release.Set();
        Assert.That(TakeCompletion(scheduler).Result, Is.EqualTo(1));
        scheduler.Start(new BuildRequest(2));
        Assert.That(TakeCompletion(scheduler).Result, Is.EqualTo(2));
    }

    [Test]
    public void CanceledBuildReportsCancellation()
    {
        using var entered = new ManualResetEventSlim();
        using var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (_, cancellationToken) =>
            {
                entered.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return 0;
            });

        scheduler.Start(new BuildRequest(7));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);
        scheduler.Cancel();

        TerrainBuildCompletion<BuildRequest, int> completion = TakeCompletion(scheduler);
        Assert.That(completion.Request.Value, Is.EqualTo(7));
        Assert.That(completion.WasCanceled, Is.True);
        Assert.That(completion.Failure, Is.Null);
    }

    [Test]
    public void CancellationInsideParallelForIsReportedAsCancellation()
    {
        using var entered = new ManualResetEventSlim();
        using var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (_, cancellationToken) =>
            {
                Parallel.For(
                    0,
                    64,
                    _ =>
                    {
                        entered.Set();
                        cancellationToken.WaitHandle.WaitOne();
                        cancellationToken.ThrowIfCancellationRequested();
                    });
                return 0;
            });

        scheduler.Start(new BuildRequest(3));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);
        scheduler.Cancel();

        TerrainBuildCompletion<BuildRequest, int> completion = TakeCompletion(scheduler);
        Assert.That(completion.WasCanceled, Is.True);
        Assert.That(completion.Failure, Is.Null);
    }

    [Test]
    public void FailureIsReportedWithItsException()
    {
        using var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (_, _) => throw new InvalidOperationException("boom"));

        scheduler.Start(new BuildRequest(4));
        TerrainBuildCompletion<BuildRequest, int> completion = TakeCompletion(scheduler);
        Assert.That(completion.WasCanceled, Is.False);
        Assert.That(completion.Failure, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void DisposeDoesNotWaitForTheRunningBuild()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var scheduler = new TerrainBuildScheduler<BuildRequest, int>(
            (_, _) =>
            {
                entered.Set();
                release.Wait();
                return 0;
            });

        scheduler.Start(new BuildRequest(9));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(2)), Is.True);

        Stopwatch dispose = Stopwatch.StartNew();
        scheduler.Dispose();
        Assert.That(dispose.ElapsedMilliseconds, Is.LessThan(50));
        Assert.Throws<ObjectDisposedException>(() => scheduler.Start(new BuildRequest(10)));
        release.Set();
    }

    private static TerrainBuildCompletion<TRequest, TResult> TakeCompletion<TRequest, TResult>(
        TerrainBuildScheduler<TRequest, TResult> scheduler)
        where TRequest : class
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (scheduler.TryTakeCompleted(out TerrainBuildCompletion<TRequest, TResult> completion))
            {
                return completion;
            }

            Thread.Sleep(1);
        }

        Assert.Fail("Terrain build did not complete within the test timeout.");
        throw new InvalidOperationException("Unreachable");
    }
}
