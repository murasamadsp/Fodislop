using System;
using System.Collections.Generic;

namespace VContainer.Internal
{
    internal static class ListPool<T>
    {
        const int DefaultCapacity = 32;

        private static readonly Stack<List<T>> _pool = new Stack<List<T>>(4);

        internal readonly struct BufferScope : IDisposable
        {
            private readonly List<T> _buffer;

            public BufferScope(List<T> buffer)
            {
                _buffer = buffer;
            }

            public void Dispose()
            {
                Release(_buffer);
            }
        }

        /// <returns></returns>
        internal static List<T> Get()
        {
            lock (_pool)
            {
                if (_pool.Count == 0)
                {
                    return new List<T>(DefaultCapacity);
                }

                return _pool.Pop();
            }
        }

        /// <param name="buffer"></param>
        /// <returns></returns>
        internal static BufferScope Get(out List<T> buffer)
        {
            buffer = Get();
            return new BufferScope(buffer);
        }

        /// <param name="buffer"></param>
        internal static void Release(List<T> buffer)
        {
            buffer.Clear();
            lock (_pool)
            {
                _pool.Push(buffer);
            }
        }
    }
}
