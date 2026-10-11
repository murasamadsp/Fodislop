#nullable enable

using System;
using System.Threading.Tasks;
using Kern.World.Lighting;
using NUnit.Framework;

namespace Kern.Tests.Editor;

public sealed class LightingOrientationValidationGateTests
{
    [Test]
    public async Task ConcurrentWaiters_CompleteOnlyAfterValidationSucceeds()
    {
        var gate = new LightingFieldOrientationValidator.LightingOrientationValidationGate();
        var first = gate.Begin();
        var second = gate.Begin();

        Assert.That(gate.IsPending, Is.True);
        Assert.That(gate.IsValidated, Is.False);

        gate.MarkValidated();
        await first;
        await second;

        Assert.That(gate.IsPending, Is.False);
        Assert.That(gate.IsValidated, Is.True);
        await gate.Begin();
    }

    [Test]
    public async Task FailedReadback_FailsEveryWaiterAndNeverMarksValidated()
    {
        var gate = new LightingFieldOrientationValidator.LightingOrientationValidationGate();
        var first = gate.Begin();
        var second = gate.Begin();
        var failure = new InvalidOperationException("simulated readback failure");
        gate.Fail(failure);

        Exception? firstFailure = await CaptureFailure(first);
        Exception? secondFailure = await CaptureFailure(second);

        Assert.That(firstFailure, Is.SameAs(failure));
        Assert.That(secondFailure, Is.SameAs(failure));
        Assert.That(gate.IsPending, Is.False);
        Assert.That(gate.IsValidated, Is.False);
        Assert.That(gate.IsFailed, Is.True);
        Assert.Throws<InvalidOperationException>(gate.ThrowIfFailed);
    }

    private static async Task<Exception?> CaptureFailure(Cysharp.Threading.Tasks.UniTask operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
