#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Core.Lifecycle;

/// <summary>
/// One entry of the typed MainGame scene contract: a serialized, concrete
/// manager reference authored on the composition root.
///
/// The editor one-way migrator fills bindings from the authored hierarchy.
/// Missing or invalid bindings are hard scene-contract failures; there is no
/// runtime name-based fallback.
/// </summary>
[Serializable]
public sealed class ManagerBinding
{
    /// <summary>The <see cref="Type.AssemblyQualifiedName"/> of the managed MonoBehaviour.</summary>
    [SerializeField]
    private string? _managerType;

    [SerializeField]
    private string? _serviceGroup;

    [SerializeField]
    private MonoBehaviour? _target;

    public ManagerBinding(string managerType, string serviceGroup, MonoBehaviour target)
    {
        _managerType = managerType;
        _serviceGroup = serviceGroup;
        _target = target;
    }

    public string? ManagerType => _managerType;

    public string? ServiceGroup => _serviceGroup;

    public MonoBehaviour? Target => _target;
}
