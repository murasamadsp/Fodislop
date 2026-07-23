using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    /// <summary>
    /// Thread-safe, lightweight ServiceLocator for runtime dependency resolution.
    /// Provides zero-allocation interface resolution, safe unregistration, and TryResolve support.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly ConcurrentDictionary<Type, object> _services = new();

        public static void Register<T>(T service)
            where T : class
        {
            if (service == null)
            {
                Debug.LogWarning($"[ServiceLocator] Attempted to register null instance for type '{typeof(T).Name}'.");
                return;
            }

            _services[typeof(T)] = service;
        }

        public static bool Unregister<T>()
            where T : class
        {
            return _services.TryRemove(typeof(T), out _);
        }

        public static T Resolve<T>()
            where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
            {
                return service as T;
            }

            return null;
        }

        public static bool TryResolve<T>(out T service)
            where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj) && obj is T typedService)
            {
                service = typedService;
                return true;
            }

            service = null;
            return false;
        }

        public static bool IsRegistered<T>()
            where T : class
        {
            return _services.ContainsKey(typeof(T));
        }

        public static void Clear()
        {
            _services.Clear();
        }
    }
}
