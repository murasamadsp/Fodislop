using System;

namespace VContainer
{
    public sealed class VContainerException : Exception
    {
        public readonly Type InvalidType;

        public VContainerException(Type invalidType, string message)
            : base(message)
        {
            InvalidType = invalidType;
        }

        public VContainerException()
            : base()
        {
        }

        public VContainerException(string message)
            : base(message)
        {
        }

        public VContainerException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
