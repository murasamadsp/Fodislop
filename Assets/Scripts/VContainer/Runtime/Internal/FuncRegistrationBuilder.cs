using System;

namespace VContainer.Internal
{
    internal sealed class FuncRegistrationBuilder : RegistrationBuilder
    {
        private readonly Func<IObjectResolver, object> implementationProvider;

        public FuncRegistrationBuilder(
            Func<IObjectResolver, object> implementationProvider,
            Type implementationType,
            Lifetime lifetime)
            : base(implementationType, lifetime)
        {
            this.implementationProvider = implementationProvider;
        }

        public override Registration Build()
        {
            var spawner = new FuncInstanceProvider(implementationProvider);
            return new Registration(ImplementationType, Lifetime, InterfaceTypes, spawner, Key);
        }
    }
}
