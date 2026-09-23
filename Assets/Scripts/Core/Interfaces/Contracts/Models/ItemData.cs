#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Models;
public class ItemData
{
    public string Name { get; set; }
    public Color IconColor { get; set; }
    public long Quantity { get; set; }
    public string Description { get; set; } = string.Empty;
    public ItemType ItemType { get; set; }
    public Texture2D? Icon { get; set; }

    public ItemData(string name, Color iconColor, int quantity) : this(name, iconColor, (long)quantity)
    {
    }

    public ItemData(string name, Color iconColor, long quantity)
    {
        Name = name;
        IconColor = iconColor;
        Quantity = quantity;
    }

    public ItemData Clone() => new ItemData(Name, IconColor, Quantity)
    {
        Description = Description,
        ItemType = ItemType,
        Icon = Icon,
    };
}
