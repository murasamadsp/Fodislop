#nullable enable

using System;
using UnityEngine;
using VContainer;

namespace Fodinae.Core
{
    public static class ServiceLocator
    {
        private static IObjectResolver? _resolver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _resolver = null;
        }

        public static bool IsInitialized => _resolver != null;

        public static void Initialize(IObjectResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>
        /// Injects [Inject] members into a runtime-created object (AddComponent/from code).
        /// Runtime-created components never reach GameLifetimeScope's startup injection scan.
        /// </summary>
        public static void Inject(object instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (_resolver == null)
            {
                throw new InvalidOperationException(
                    "ServiceLocator.Inject was called before the VContainer resolver was initialized.");
            }

            _resolver.Inject(instance);
        }

        public static T Resolve<T>()
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

        public static bool TryResolve<T>(out T? instance)
            where T : class
        {
            if (_resolver == null)
            {
                instance = null;
                return false;
            }

            try
            {
                instance = _resolver.Resolve<T>();
                return instance != null;
            }
            catch
            {
                instance = null;
                return false;
            }
        }

        public static T? TryResolve<T>()
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
            catch
            {
                return null;
            }
        }
    }
}
