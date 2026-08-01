using System;
using System.Collections.Generic;
using System.Threading;
#if VCONTAINER_UNITASK_INTEGRATION
using Cysharp.Threading.Tasks;
#endif

namespace VContainer.Unity
{
    internal sealed class StartableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IEnumerable<IStartable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public StartableLoopItem(
            IEnumerable<IStartable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            foreach (var x in entries)
            {
                try
                {
                    x.Start();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return false;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class PostStartableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IEnumerable<IPostStartable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public PostStartableLoopItem(
            IEnumerable<IPostStartable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            foreach (var x in entries)
            {
                try
                {
                    x.PostStart();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return false;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class FixedTickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<IFixedTickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public FixedTickableLoopItem(
            IReadOnlyList<IFixedTickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].FixedTick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class PostFixedTickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<IPostFixedTickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public PostFixedTickableLoopItem(
            IReadOnlyList<IPostFixedTickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].PostFixedTick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class TickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<ITickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public TickableLoopItem(
            IReadOnlyList<ITickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].Tick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class PostTickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<IPostTickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public PostTickableLoopItem(
            IReadOnlyList<IPostTickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].PostTick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class LateTickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<ILateTickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public LateTickableLoopItem(
            IReadOnlyList<ILateTickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].LateTick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

    internal sealed class PostLateTickableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IReadOnlyList<IPostLateTickable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private bool disposed;

        public PostLateTickableLoopItem(
            IReadOnlyList<IPostLateTickable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                try
                {
                    entries[i].PostLateTick();
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
            }

            return !disposed;
        }

        public void Dispose() => disposed = true;
    }

#if VCONTAINER_UNITASK_INTEGRATION || UNITY_2021_3_OR_NEWER
    internal sealed class AsyncStartableLoopItem : IPlayerLoopItem, IDisposable
    {
        private readonly IEnumerable<IAsyncStartable> entries;
        private readonly EntryPointExceptionHandler exceptionHandler;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private bool disposed;

        public AsyncStartableLoopItem(
            IEnumerable<IAsyncStartable> entries,
            EntryPointExceptionHandler exceptionHandler)
        {
            this.entries = entries;
            this.exceptionHandler = exceptionHandler;
        }

        public bool MoveNext()
        {
            if (disposed)
            {
                return false;
            }

            foreach (var x in entries)
            {
#if VCONTAINER_UNITASK_INTEGRATION
                var task = x.StartAsync(cts.Token);
                if (exceptionHandler != null)
                {
                    task.Forget(ex => exceptionHandler.Publish(ex));
                }
                else
                {
                    task.Forget();
                }
#else
                try
                {
                    var task = x.StartAsync(cts.Token);
                    _ = task.Forget(exceptionHandler);
                }
                catch (Exception ex)
                {
                    if (exceptionHandler == null)
                    {
                        throw;
                    }

                    exceptionHandler.Publish(ex);
                }
#endif
            }

            return false;
        }

        public void Dispose()
        {
            lock (entries)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }

            cts.Cancel();
            cts.Dispose();
        }
    }
#endif
}
