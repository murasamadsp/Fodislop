using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VContainer.Internal
{
    internal sealed class InstanceProvider : IInstanceProvider
    {
        private readonly IInjector injector;
        private readonly IReadOnlyList<IInjectParameter> customParameters;

        public InstanceProvider(
            IInjector injector,
            IReadOnlyList<IInjectParameter> customParameters = null)
        {
            this.injector = injector;
            this.customParameters = customParameters;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object SpawnInstance(IObjectResolver resolver)
            => injector.CreateInstance(resolver, customParameters);
    }
}
