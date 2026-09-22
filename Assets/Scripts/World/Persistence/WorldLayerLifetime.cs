#nullable enable

namespace Kern.Persistence;

/// <summary>
/// Замок ввода-вывода и признак закрытия, общие для слоя и его частей.
/// </summary>
///
/// Замок один на весь слой: файл, читатель и таблица смещений обязаны жить в
/// одной критической секции. Признак закрытия тоже общий — загрузчик чанков
/// дожидается диска в пуле потоков и по нему решает, можно ли ещё что-то
/// трогать.
internal sealed class WorldLayerLifetime
{
    public object IoLock { get; } = new();

    public bool Disposed { get; private set; }

    /// <summary>Вызывать только под <see cref="IoLock"/>.</summary>
    public void MarkDisposed() => Disposed = true;
}
