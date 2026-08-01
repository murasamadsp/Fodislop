using System;
using System.Collections.Generic;
using UnityEngine;

namespace VContainer.Unity
{
    internal sealed class NewGameObjectProvider : IInstanceProvider
    {
        private readonly Type componentType;
        private readonly IInjector injector;
        private readonly IReadOnlyList<IInjectParameter> customParameters;
        private readonly string newGameObjectName;
        private ComponentDestination destination;

        public NewGameObjectProvider(
            Type componentType,
            IInjector injector,
            IReadOnlyList<IInjectParameter> customParameters,
            ComponentDestination destination,
            string newGameObjectName = null)
        {
            this.componentType = componentType;
            this.customParameters = customParameters;
            this.injector = injector;
            this.destination = destination;
            this.newGameObjectName = newGameObjectName;
        }

        public object SpawnInstance(IObjectResolver resolver)
        {
            var name = string.IsNullOrEmpty(newGameObjectName)
                ? componentType.Name
                : newGameObjectName;
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);

            var parent = destination.GetParent(resolver);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent);
            }

            var component = gameObject.AddComponent(componentType);

            injector.Inject(component, resolver, customParameters);
            destination.ApplyDontDestroyOnLoadIfNeeded(component);

            component.gameObject.SetActive(true);
            return component;
        }
    }
}
