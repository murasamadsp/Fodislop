#nullable enable

using UnityEngine;

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
