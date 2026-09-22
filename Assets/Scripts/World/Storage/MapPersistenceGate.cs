#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern.World;

// Взаимное исключение записи карты на диск.
//
// Отдельный тип, потому что протокол здесь неочевидный и был написан дважды
// от руки — во FlushAsync и в DisposeAsync. Семафор берётся только синхронно
// на главном потоке, а асинхронная запись отпускает его в пуле потоков, не
// возвращаясь на главный: иначе синхронный Flush на выходе из игры блокирует
// главный поток в ожидании семафора, который держит запись, ждущая этот же
// главный поток. Две копии такого правила — две возможности разойтись.
internal sealed class MapPersistenceGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Синхронный проход: главный поток ждёт, тело выполняется под захватом.
    public void Run(Action body)
    {
        _gate.Wait();
        try
        {
            body();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <param name="prepareOnMainThread">
    /// Выполняется на главном потоке под захватом и возвращает тело для пула
    /// потоков — либо <c>null</c>, если делать нечего. Снимок состояния обязан
    /// сниматься здесь: кэш меняется на главном потоке, и в пул должны уходить
    /// только запись и закрытие файла.
    /// </param>
    public async UniTask RunAsync(
        Func<Action?> prepareOnMainThread,
        CancellationToken cancellationToken = default)
    {
        await UniTask.SwitchToMainThread(cancellationToken);
        while (!_gate.Wait(0))
        {
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        bool releasedInPool = false;
        try
        {
            Action? body = prepareOnMainThread();
            if (body == null)
            {
                return;
            }

            Exception? failure = null;
            await UniTask.RunOnThreadPool(
                () =>
                {
                    try
                    {
                        body();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        releasedInPool = true;
                        _gate.Release();
                    }
                },
                configureAwait: false);

            await UniTask.SwitchToMainThread();
            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            if (!releasedInPool)
            {
                _gate.Release();
            }
        }
    }
}
