#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Fodinae.UI;

/// <summary>Projects world annotations into the existing screen-space UI panel.</summary>
public sealed class WorldLabels(UIDocument document, IGameplayCamera camera) : IWorldLabels, ILateTickable, IDisposable
{
    private readonly List<Entry> _entries = [];
    private VisualElement? _root;
    private VisualElement? _container;

    public IWorldLabel Create(bool chatBubble)
    {
        if (_root == null)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/WorldLabels")
                ?? throw new InvalidOperationException("Missing UI/WorldLabels.");
            _root = template.CloneTree();
            _root.AddToClassList("world-labels");
            _root.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Insert(0, _root);
            _container = _root.Q("WorldLabels");
        }

        // Labels are a dynamic collection, not static screen structure.
        var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
        label.AddToClassList(chatBubble ? "world-label-chat" : "world-label-name");
        _container!.Add(label);
        var entry = new Entry(this, label);
        _entries.Add(entry);
        return entry;
    }

    public void LateTick()
    {
        if (_root?.panel == null)
        {
            return;
        }

        Camera view = camera.Camera;
        foreach (Entry entry in _entries)
        {
            Vector3 viewport = view.WorldToViewportPoint(entry.Position);
            bool visible = entry.Visible && viewport.z > 0f &&
                viewport.x >= -0.15f && viewport.x <= 1.15f &&
                viewport.y >= -0.15f && viewport.y <= 1.15f;
            UIState.SetHidden(entry.Label, !visible);
            if (visible)
            {
                Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(
                    _root.panel, entry.Position, view);
                Vector2 local = _container!.WorldToLocal(panelPosition);
                entry.Label.style.translate = new Translate(local.x, local.y);
            }
        }
    }

    public void Dispose()
    {
        _entries.Clear();
        _root?.RemoveFromHierarchy();
        _root = null;
        _container = null;
    }

    private sealed class Entry(WorldLabels owner, Label label) : IWorldLabel
    {
        public Label Label { get; } = label;
        public Vector3 Position { get; private set; }
        public bool Visible { get; private set; } = true;

        public void SetText(string text) => Label.text = text;
        public void SetPosition(Vector3 position) => Position = position;
        public void SetVisible(bool visible) => Visible = visible;
        public void SetOpacity(float opacity) => Label.style.opacity = Mathf.Clamp01(opacity);

        public void Dispose()
        {
            owner._entries.Remove(this);
            Label.RemoveFromHierarchy();
        }
    }
}
