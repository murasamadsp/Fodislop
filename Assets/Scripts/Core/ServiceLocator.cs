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

        /// <summary>
        /// Injects [Inject] members into a runtime-created object (AddComponent/from code).
        /// Runtime-created components never reach GameLifetimeScope's startup injection scan.
        /// </summary>
        public static void Inject(object instance)
        {
            if (instance == null)
            {
                return;
            }

            _resolver?.Inject(instance);
        }

        public static T Resolve<T>()
            where T : class
        {
            if (_resolver == null)
            {
                Debug.LogError($"[ServiceLocator] Cannot resolve {typeof(T).Name}, _resolver is null!");
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
