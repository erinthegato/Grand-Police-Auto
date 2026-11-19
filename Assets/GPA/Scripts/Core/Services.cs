using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Minimal service registry used to decouple systems. Systems register their interface
    /// implementation on Awake and unregister on destroy. Consumers resolve by interface only.
    /// </summary>
    public static class Services
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>Raised when a service is registered or replaced.</summary>
        public static event Action<Type, object> ServiceRegistered;
        /// <summary>Raised when a service is removed.</summary>
        public static event Action<Type> ServiceUnregistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _services.Clear();
            ServiceRegistered = null;
            ServiceUnregistered = null;
        }

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _services[typeof(T)] = service;
            ServiceRegistered?.Invoke(typeof(T), service);
        }

        /// <summary>Unregisters only if the currently registered instance is <paramref name="instance"/>.</summary>
        public static void Unregister<T>(T instance) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, instance))
            {
                _services.Remove(typeof(T));
                ServiceUnregistered?.Invoke(typeof(T));
            }
        }

        public static T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s)) { service = (T)s; return true; }
            service = null;
            return false;
        }

        public static bool Has<T>() where T : class => _services.ContainsKey(typeof(T));

        /// <summary>Debug helper: snapshot of all registered service types.</summary>
        public static IEnumerable<Type> RegisteredTypes => _services.Keys;

        /// <summary>Test helper.</summary>
        public static void ClearAll() => _services.Clear();
    }
}
