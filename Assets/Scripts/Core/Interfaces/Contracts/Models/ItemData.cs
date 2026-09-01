#nullable enable

using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Core.Models
{
    /// <summary>
    /// Нейтральное описание слота инвентаря. Живёт в Core, а не в UI-моделях,
    /// чтобы Networking-слой мог описывать инвентарь без зависимости от
    /// presentation (граница слоёв по плану стабилизации DI).
    /// </summary>
    public class ItemData
    {
        public string Name { get; set; }
        public Color IconColor { get; set; }
        public int Quantity { get; set; }
        public string Description { get; set; } = string.Empty;
        public ItemType ItemType { get; set; }
        public Texture2D? Icon { get; set; }

        public ItemData(string name, Color iconColor, int quantity)
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
}
