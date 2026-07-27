using UnityEngine;
using VContainer;

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
            if (_resolver == null)
            {
                return null;
            }

            try
            {
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
