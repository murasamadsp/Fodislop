namespace Fodinae.Scripts.Core.DI
{
    public interface IServiceLocator
    {
        void Initialize(VContainer.IObjectResolver resolver);
        T Resolve<T>() where T : class;
    }
}
