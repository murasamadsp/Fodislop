using System;

namespace VContainer.Unity
{
    internal sealed class EntryPointExceptionHandler
    {
        private readonly Action<Exception> handler;

        public EntryPointExceptionHandler(Action<Exception> handler)
        {
            this.handler = handler;
        }

        public void Publish(Exception ex)
        {
            handler.Invoke(ex);
        }
    }
}
