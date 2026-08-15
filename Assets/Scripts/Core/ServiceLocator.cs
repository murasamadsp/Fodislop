#nullable enable

using System;
using UnityEngine;
using VContainer;

namespace Fodinae.Core
{
    public static class ServiceLocator
    {
        private static IObjectResolver? _resolver;

        public static bool IsInitialized => _resolver != null;

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

            if (_resolver == null)
            {
                throw new InvalidOperationException(
                    "ServiceLocator.Inject was called before the VContainer resolver was initialized.");
            }

            _resolver.Inject(instance);
        }

        public static T? Resolve<T>()
            where T : class
        {
            if (_resolver == null)
            {
                throw new InvalidOperationException(
                    $"ServiceLocator.Resolve<{typeof(T).Name}> was called before the VContainer resolver was initialized.");
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
