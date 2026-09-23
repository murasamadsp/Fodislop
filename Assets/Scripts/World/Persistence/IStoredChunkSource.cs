#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern.Persistence;

public interface IStoredChunkSource<T>
    where T : unmanaged
{
    /// <summary>
    /// Visits chunks present in the existing layer file without adding them to
    /// the resident chunk cache. Each callback receives one RLE value and its
    /// consecutive cell count from the current chunk.
    /// </summary>
    UniTask VisitStoredChunkRunsAsync(
        Action<int, T, int> runVisitor,
        Action<int, int>? progress,
        CancellationToken cancellationToken = default);
}
