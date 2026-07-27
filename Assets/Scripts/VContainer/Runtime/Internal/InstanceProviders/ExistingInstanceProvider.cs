using System.Runtime.CompilerServices;

namespace VContainer.Internal
{
    internal sealed class ExistingInstanceProvider : IInstanceProvider
    {
        private readonly object implementationInstance;

        public ExistingInstanceProvider(object implementationInstance)
        {
            this.implementationInstance = implementationInstance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object SpawnInstance(IObjectResolver resolver) => implementationInstance;
    }
}
