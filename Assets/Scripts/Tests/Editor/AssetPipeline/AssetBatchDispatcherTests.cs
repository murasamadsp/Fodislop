#nullable enable

using System;
using System.Threading;
using Kern;
using NUnit.Framework;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class AssetBatchDispatcherTests
{
    [Test]
    public void RequestAssetBytesAsync_ConnectionProviderThrows_RemovesFailedRequest()
    {
        using var dispatcher = new AssetBatchDispatcher();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            dispatcher.RequestAssetBytesAsync(
                    "cells/1",
                    string.Empty,
                    CancellationToken.None,
                    () => throw new InvalidOperationException("connection provider failed"),
                    () => null)
                .GetAwaiter()
                .GetResult());

        Assert.That(exception.Message, Is.EqualTo("connection provider failed"));
        Assert.That(dispatcher.PendingCount, Is.Zero);
    }
}
