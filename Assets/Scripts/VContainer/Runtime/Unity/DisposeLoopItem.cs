using System;

namespace VContainer.Unity
{
    internal sealed class AsyncLoopItem : IPlayerLoopItem
    {
        private readonly Action action;

        public AsyncLoopItem(Action action)
        {
            this.action = action;
        }

        public bool MoveNext()
        {
            action();
            return false;
        }
    }
}
