#nullable enable

using System;
using Kern.Core.Localization;
using Kern.Core.Models;
using MinesServer.Data;
using UnityEngine.UIElements;

namespace Kern.UI.Inventory;

/// <summary>
/// Controls creation, population, and visibility of the inventory tooltip.
/// </summary>
internal sealed class InventoryTooltipController
{
    private readonly VisualElement _tooltipWrapper;
    private readonly Label _tooltipName;
    private readonly Label _tooltipDesc;
    private readonly ILocalizationService _loc;

    public InventoryTooltipController(VisualElement root, ILocalizationService loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));

        _tooltipWrapper = new VisualElement();
        _tooltipWrapper.AddToClassList("inv-tooltip-wrapper");
        _tooltipWrapper.style.display = DisplayStyle.None;

        var tooltipBg = new VisualElement();
        tooltipBg.AddToClassList("inv-tooltip-bg");

        _tooltipName = new Label();
        _tooltipName.AddToClassList("inv-tooltip-name");
        tooltipBg.Add(_tooltipName);

        _tooltipDesc = new Label();
        _tooltipDesc.AddToClassList("inv-tooltip-desc");
        tooltipBg.Add(_tooltipDesc);

        _tooltipWrapper.Add(tooltipBg);
        root.Add(_tooltipWrapper);
    }

    public void ShowSlotTooltip(ItemData item)
    {
        _tooltipName.text = item.Name;
        _tooltipDesc.text = item.Description ?? string.Empty;
        _tooltipWrapper.style.display = DisplayStyle.Flex;
    }

    public void ShowItemInfo(ItemData item)
    {
        _tooltipName.text = _loc.Get(
            "inventory.tooltip_item",
            item.Name ?? item.ItemType.ToString(),
            item.ItemType,
            item.Quantity);
        _tooltipDesc.text = _loc.Get("inventory.tooltip_type", item.ItemType) +
            "\n" +
            (item.Description ?? _loc.Get("inventory.no_description"));
        _tooltipWrapper.style.display = DisplayStyle.Flex;
    }

    public void HideTooltip()
    {
        _tooltipWrapper.style.display = DisplayStyle.None;
    }
}
