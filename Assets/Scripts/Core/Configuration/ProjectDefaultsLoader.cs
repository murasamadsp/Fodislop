#nullable enable

using System;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core;

public static class ProjectDefaultsLoader
{
    public static IProjectDefaults LoadRequired()
    {
        ProjectDefaults[] assets = Resources.LoadAll<ProjectDefaults>(ProjectRuntimeContracts.ResourcePaths.Configuration);
        if (assets.Length != 1)
        {
            throw new InvalidOperationException(
                $"Exactly one ProjectDefaults asset is required under " +
                $"Resources/{ProjectRuntimeContracts.ResourcePaths.Configuration}; " +
                $"found {assets.Length}.");
        }

        ProjectDefaults asset = assets[0];
        if (!string.Equals(asset.name, ProjectDefaults.ResourceName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Project defaults asset must be named '{ProjectDefaults.ResourceName}', " +
                $"not '{asset.name}'.");
        }

        return asset.CreateSnapshot();
    }
}
