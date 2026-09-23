#nullable enable

using System;
using Kern.Core.Localization;
using Kern.Core.Models;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Inventory;

/// <summary>
/// Drives the shared tooltip card (ItemTooltipCard from Inventory.uxml):
/// fills the icon, name, type badge, and description for the selected item.
/// </summary>
internal sealed class InventoryTooltipController
{
    private readonly VisualElement _card;
    private readonly Image _icon;
    private readonly Label _name;
    private readonly Label _type;
    private readonly Label _desc;
    private readonly ILocalizationService _loc;

    public InventoryTooltipController(VisualElement root, ILocalizationService loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        _card = root.Q<VisualElement>("ItemTooltipCard") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] ItemTooltipCard missing from Inventory.uxml.");
        _icon = _card.Q<Image>("TooltipIcon") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipIcon missing from ItemTooltipCard.");
        _name = _card.Q<Label>("TooltipName") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipName missing from ItemTooltipCard.");
        _type = _card.Q<Label>("TooltipType") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipType missing from ItemTooltipCard.");
        _desc = _card.Q<Label>("TooltipDesc") ?? throw new InvalidOperationException(
            "[InventoryTooltipController] TooltipDesc missing from ItemTooltipCard.");
    }

    public void ShowItemInfo(ItemData item, Texture2D? icon)
    {
        if (icon != null)
        {
            _icon.image = icon;
            _icon.style.display = DisplayStyle.Flex;
        }
        else
        {
            _icon.image = null;
            _icon.style.display = DisplayStyle.None;
        }

        _name.text = _loc.Get(
            "inventory.tooltip_item",
            item.Name ?? item.ItemType.ToString(),
            item.ItemType,
            item.Quantity);
        _type.text = item.ItemType.ToString();
        _desc.text = item.Description ?? string.Empty;
        _card.style.display = DisplayStyle.Flex;
    }

    public void HideTooltip()
    {
        _card.style.display = DisplayStyle.None;
    }
}