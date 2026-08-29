#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Localization;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Inventory.View;

/// <summary>
/// Manages drag-and-drop operations, floating drag element and right-click context menu in the Inventory UI.
/// </summary>
public sealed class InventoryDragAndContextMenu
{
    private const int ICON_SIZE = 36;

    private VisualElement? _floatingItem;
    private int _dragFromSlot = -1;
    private ItemData? _draggedItem;
    private VisualElement? _contextMenu;

    public bool IsDragging => _dragFromSlot >= 0;

    public void StartDrag(
        int slotIndex,
        ItemData item,
        Vector2 mousePosition,
        VisualElement root,
        VisualElement cell,
        EventCallback<MouseMoveEvent> onDragMove,
        EventCallback<MouseUpEvent> onDragDrop)
    {
        _dragFromSlot = slotIndex;
        _draggedItem = item;
        cell.RemoveFromClassList("inv-cell--highlight");

        HideContextMenu(root);

        _floatingItem = new VisualElement();
        _floatingItem.AddToClassList("inv-floating");
        if (item.Icon != null)
        {
            _floatingItem.style.backgroundImage = new StyleBackground(item.Icon);
            _floatingItem.style.backgroundColor = Color.clear;
        }
        else
        {
            _floatingItem.style.backgroundColor = Color.gray;
        }

        _floatingItem.pickingMode = PickingMode.Ignore;

        root.Add(_floatingItem);
        UpdateFloatingPosition(mousePosition);

        root.RegisterCallback<MouseMoveEvent>(onDragMove);
        root.RegisterCallback<MouseUpEvent>(onDragDrop);
    }

    public void UpdateFloatingPosition(Vector2 mousePos)
    {
        if (_floatingItem != null)
        {
            _floatingItem.style.left = mousePos.x - (ICON_SIZE / 2);
            _floatingItem.style.top = mousePos.y - (ICON_SIZE / 2);
        }
    }

    public void Drop(
        Vector2 mousePosition,
        VisualElement root,
        IInventoryModel model,
        Dictionary<int, List<VisualElement>> slotElements,
        EventCallback<MouseMoveEvent> onDragMove,
        EventCallback<MouseUpEvent> onDragDrop)
    {
        if (_dragFromSlot < 0 || _floatingItem == null)
        {
            return;
        }

        root.UnregisterCallback<MouseMoveEvent>(onDragMove);
        root.UnregisterCallback<MouseUpEvent>(onDragDrop);

        var target = FindSlotUnderMouse(mousePosition, slotElements);
        if (target >= 0 && target != _dragFromSlot)
        {
            if (InventoryModel.CanStack(_draggedItem!, model.GetSlot(target)))
            {
                model.TryStackSlots(_dragFromSlot, target);
            }
            else
            {
                model.SwapSlots(_dragFromSlot, target);
            }
        }

        root.Remove(_floatingItem);
        _floatingItem = null;
        _dragFromSlot = -1;
        _draggedItem = null;
    }

    public void Cleanup(VisualElement? root, EventCallback<MouseMoveEvent> onDragMove, EventCallback<MouseUpEvent> onDragDrop)
    {
        if (root != null)
        {
            root.UnregisterCallback<MouseMoveEvent>(onDragMove);
            root.UnregisterCallback<MouseUpEvent>(onDragDrop);
            HideContextMenu(root);
        }

        if (_floatingItem != null && _floatingItem.parent != null)
        {
            _floatingItem.RemoveFromHierarchy();
            _floatingItem = null;
        }
    }

    private static int FindSlotUnderMouse(Vector2 mousePos, Dictionary<int, List<VisualElement>> slotElements)
    {
        foreach (var kvp in slotElements)
        {
            foreach (var cell in kvp.Value)
            {
                if (cell.worldBound.Contains(mousePos))
                {
                    return kvp.Key;
                }
            }
        }

        return -1;
    }

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
