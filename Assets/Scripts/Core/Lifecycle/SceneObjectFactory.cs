#nullable enable

using System;
using UnityEngine;
using VContainer;

namespace Fodinae.Core.Lifecycle;

public enum RuntimeOwner
{
    General,
    Robots,
    Buildings,
    Vfx,
    FloatingUI,
    AudioEvents,
}

public interface ISceneObjectFactory
{
    Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General);

    GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General);

    T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General)
        where T : MonoBehaviour;
}

public sealed class SceneObjectFactory(
    ContentSceneRoot sceneRoot,
    IObjectResolver resolver) : ISceneObjectFactory
{
    public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) =>
        sceneRoot.GetRuntimeOwner(owner);

    public GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Runtime object name is required.", nameof(name));
        }

        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(sceneRoot.GetRuntimeOwner(owner), false);
        return gameObject;
    }

    public T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General)
        where T : MonoBehaviour
    {
        GameObject gameObject = Create(name, owner);
        gameObject.SetActive(false);
        T component = gameObject.AddComponent<T>();
        resolver.Inject(component);
        gameObject.SetActive(true);
        return component;
    }
}
