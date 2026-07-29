#nullable enable

using UnityEngine;
using VContainer;

namespace Fodinae.Core
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

        public static T? Resolve<T>()
            where T : class
        {
            if (_resolver == null)
            {
                if (typeof(MonoBehaviour).IsAssignableFrom(typeof(T)))
                {
                    return Object.FindAnyObjectByType(typeof(T)) as T;
                }

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
