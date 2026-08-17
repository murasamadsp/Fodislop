#nullable enable

using System;
using UnityEngine;

namespace Fodinae.Core;

public static class UnityRenderLayerContracts
{
    public static int RequireWorldUIGameObjectLayer()
    {
        int layer = LayerMask.NameToLayer(ProjectRuntimeContracts.RequiredLayers.WorldUI);
        if (layer < 0)
        {
            throw new InvalidOperationException(
                $"Required Unity GameObject layer '{ProjectRuntimeContracts.RequiredLayers.WorldUI}' is missing.");
        }

        return layer;
    }

    public static int RequireWorldUISortingLayer()
    {
        int layerId = SortingLayer.NameToID(ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer);
        if (layerId == 0 && !string.Equals(
                SortingLayer.IDToName(layerId),
                ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Required Unity Sorting Layer '{ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer}' is missing.");
        }

        return layerId;
    }

    public static void ApplyWorldUI(Renderer renderer, int sortingOrder)
    {
        renderer.gameObject.layer = RequireWorldUIGameObjectLayer();
        renderer.sortingLayerName = ProjectRuntimeContracts.RequiredLayers.WorldUISortingLayer;
        renderer.sortingOrder = sortingOrder;
    }
}
