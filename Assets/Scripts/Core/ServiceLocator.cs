using VContainer;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public static class ServiceLocator
    {
        private static IObjectResolver _resolver;

        public static void Initialize(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public static T Resolve<T>()
            where T : class
        {
            try
            {
                if (_resolver == null)
                {
                    Debug.LogError($"[ServiceLocator] Cannot resolve {typeof(T).Name}, _resolver is null!");
                    return null;
                }
                return _resolver.Resolve<T>();
            }
            catch (VContainerException ex)
            {
                Debug.LogException(ex);
                throw;
            }
        }
    }
}
