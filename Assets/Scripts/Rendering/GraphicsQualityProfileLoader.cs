#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Rendering;

public static class GraphicsQualityProfileLoader
{
    public static GraphicsQualityProfile LoadRequired()
    {
        GraphicsQualityProfile profile =
            Resources.Load<GraphicsQualityProfile>(
                ProjectRuntimeContracts.ResourcePaths.GraphicsQualityProfile) ??
            throw new InvalidOperationException(
                $"Required graphics profile Resources/" +
                $"{ProjectRuntimeContracts.ResourcePaths.GraphicsQualityProfile}.asset is missing.");
        profile.Validate();
        return profile;
    }
}
