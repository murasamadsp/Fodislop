#nullable enable

namespace Kern.Core.Interfaces;

/// <summary>
/// Implemented by network packet processors and underlying reactive models that
/// support batching multiple packet updates before flushing visual/state notifications.
/// </summary>
public interface IBatchAwareProcessor
{
    void BeginBatch();
    void EndBatch();
}
