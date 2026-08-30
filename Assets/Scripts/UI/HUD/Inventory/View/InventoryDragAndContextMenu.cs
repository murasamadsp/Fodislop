#nullable enable

using System;
using Fodinae.Core.Localization;
using Fodinae.Core.Models;
using Fodinae.UI.HUD.Inventory.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Inventory.View;

/// <summary>
/// Right-click context menu for inventory slots (use / show info).
/// Drag-and-drop was removed deliberately.
/// </summary>
public sealed class InventoryDragAndContextMenu
{
    private VisualElement? _contextMenu;

    public void ShowContextMenu(
        Vector2 mousePos,
        int slotIndex,
        VisualElement root,
        IInventoryModel model,
        ILocalizationService loc,
        Action<ItemData> showItemInfo)
    {
        var item = model.GetSlot(slotIndex);
        if (item == null)
        {
            return;
        }

        _contextMenu = new VisualElement();
        _contextMenu.name = "ContextMenu";
        _contextMenu.AddToClassList("inv-context-menu");
        _contextMenu.style.left = mousePos.x;
        _contextMenu.style.top = mousePos.y;
        _contextMenu.pickingMode = PickingMode.Position;

        AddContextMenuItem(loc.Get("inventory.context_use"), () =>
        {
            model.SelectSlot(slotIndex);
            model.UseSelectedItem();
            HideContextMenu(root);
        });

        AddContextMenuItem(loc.Get("inventory.context_info"), () =>
        {
            showItemInfo(item);
            HideContextMenu(root);
        });

        root.Add(_contextMenu);

        root.RegisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
        root.RegisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
    }

    private void AddContextMenuItem(string labelText, Action onClick)
    {
        var btn = new Button(onClick);
        btn.text = labelText;
        btn.AddToClassList("inv-context-btn");
        _contextMenu?.Add(btn);
    }

    public void HideContextMenu(VisualElement? root)
    {
        if (_contextMenu != null)
        {
            _contextMenu.RemoveFromHierarchy();
            _contextMenu = null;
        }

        root?.UnregisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
        root?.UnregisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
    }

    public void Cleanup(VisualElement? root)
    {
        HideContextMenu(root);
    }

    private void OnContextMenuOutsideClick(MouseDownEvent evt)
    {
        if (_contextMenu != null && !_contextMenu.worldBound.Contains(evt.mousePosition))
        {
            if (evt.currentTarget is VisualElement root)
            {
                HideContextMenu(root);
            }
        }
    }

    private void OnContextMenuEscape(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape)
        {
            if (evt.currentTarget is VisualElement root)
            {
                HideContextMenu(root);
            }
        }
    }
}
