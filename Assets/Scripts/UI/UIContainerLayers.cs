#nullable enable

using System;
using UnityEngine.UIElements;

namespace Fodinae.UI;

public static class UIContainerLayers
{
    public const string Hud = "LayerHud";
    public const string Modal = "LayerModal";
    public const string Blocking = "LayerBlocking";
    public const string Debug = "LayerDebug";

    public static VisualElement Get(UIDocument document, string layerName)
    {
        VisualElement root = document.rootVisualElement;
        string[] layerNames = [Hud, Modal, Blocking, Debug];
        foreach (string name in layerNames)
        {
            if (root.Q<VisualElement>(name) != null)
            {
                continue;
            }

            VisualElement layer = new()
            {
                name = name,
                pickingMode = PickingMode.Ignore,
            };
            layer.AddToClassList("ui-layer");
            root.Add(layer);
        }

        VisualElement? requestedLayer = root.Q<VisualElement>(layerName);
        if (requestedLayer == null)
        {
            throw new InvalidOperationException($"Unknown UI layer '{layerName}'.");
        }

        return requestedLayer;
    }

    public static void SetInteractive(UIDocument document, string layerName, bool interactive)
    {
        // Interactive overlays are attached directly to the panel root. A full-screen
        // VisualElement ancestor participates in UI Toolkit hit testing and can swallow
        // pointer events from nested buttons and ScrollViews.
    }
}
