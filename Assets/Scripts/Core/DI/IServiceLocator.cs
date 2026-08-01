#nullable enable

namespace Fodinae.Core.DI
{
    public interface IServiceLocator
    {
        void Initialize(VContainer.IObjectResolver resolver);
        T Resolve<T>()
            where T : class;
    }
}
