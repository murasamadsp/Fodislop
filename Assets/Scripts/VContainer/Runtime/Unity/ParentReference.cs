#nullable enable

using System;
using UnityEngine;

namespace VContainer.Unity
{
    [Serializable]
    public struct ParentReference : ISerializationCallbackReceiver
    {
        [SerializeField]
        public string TypeName;

        [NonSerialized]
        public LifetimeScope? Object;

        public Type? Type { get; private set; }

        ParentReference(Type type)
        {
            Type = type;
            TypeName = type.AssemblyQualifiedName ?? type.FullName ?? throw new InvalidOperationException(
                $"Type '{type}' has no serializable name.");
            Object = null;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            TypeName = Type?.AssemblyQualifiedName ?? string.Empty;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            Type = string.IsNullOrWhiteSpace(TypeName)
                ? null
                : System.Type.GetType(TypeName, throwOnError: false);
        }

        public static ParentReference Create<T>() where T : LifetimeScope
        {
            return new ParentReference(typeof(T));
        }
    }
}
