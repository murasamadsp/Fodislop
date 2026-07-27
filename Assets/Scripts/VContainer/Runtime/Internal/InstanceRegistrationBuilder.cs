using System.Runtime.CompilerServices;

namespace VContainer.Internal
{
    internal sealed class InstanceRegistrationBuilder : RegistrationBuilder
    {
        private readonly object implementationInstance;

        public InstanceRegistrationBuilder(object implementationInstance)
            : base(implementationInstance.GetType(), Lifetime.Singleton)
        {
            this.implementationInstance = implementationInstance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Registration Build()
        {
            var spawner = new ExistingInstanceProvider(implementationInstance);
            return new Registration(ImplementationType, Lifetime, InterfaceTypes, spawner, Key);
        }
    }
}
